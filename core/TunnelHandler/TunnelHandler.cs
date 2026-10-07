using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace MoSaProxy;

/// <summary>
/// Handles HTTPS-style tunnels (HTTP/1.1 CONNECT) at the connection level. Kestrel cannot upgrade a CONNECT
/// request, so this runs ahead of Kestrel's HTTP parser: it peeks at the start of each connection, takes over
/// connections that begin with "CONNECT ", and hands everything else to Kestrel untouched.
/// <para>
/// A tunnel is either blind (encrypted bytes are relayed without being read, so only host, port and byte counts
/// are known) or intercepted (this class does the TLS handshake with the client using a generated certificate,
/// then passes the decrypted connection on to Kestrel as ordinary HTTP/1.1 traffic).
/// </para>
/// </summary>
/// <param name="stopping">Signalled when the proxy starts shutting down: idle connections and quiet tunnels end, busy ones may finish.</param>
/// <param name="shutdown">Signalled when the drain period is over: whatever is left is aborted.</param>
public sealed class TunnelHandler(
	IExchangeLog log, ISessionCapture capture, ProxyOptions options, CancellationToken stopping, CancellationToken shutdown,
	ProxyActivity activity, ResourceLimitMonitor limits, CertificateAuthority? authority = null) {

	/// <summary>Key in the connection's Items holding the https:// base URI of an intercepted connection.</summary>
	public const string InterceptTargetKey = "MoSaProxy.intercept-target";

	const int MaxHeadBytes = 64 * 1024;
	static readonly byte[] ConnectPrefix = Encoding.ASCII.GetBytes("CONNECT ");
	static readonly byte[] Established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

	public async Task HandleAsync(ConnectionContext connection, ConnectionDelegate next) {
		var input = connection.Transport.Input;

		// A connection that has not sent anything yet (browsers open spare ones in advance) must not hold up a shutdown.
		bool startsWithConnect;
		try {
			using var waiting = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed, stopping);
			startsWithConnect = await StartsWithConnectAsync(input, waiting.Token);
		}
		catch (OperationCanceledException) {
			return;
		}
		if (!startsWithConnect) {
			await next(connection);
			return;
		}

		long started = Stopwatch.GetTimestamp();
		IExchangeCapture exchangeCapture;
		try { exchangeCapture = capture.BeginExchange(); }
		catch (Exception ex) {
			Console.Error.WriteLine($"Capture unavailable: {ex.Message}");
			exchangeCapture = NullExchangeCapture.Instance;
		}

		string target = "", protocol = "HTTP/1.1", host = "", mode = "tunnel";
		int port = 0, status = 0;
		var headers = new List<KeyValuePair<string, string>>();
		long toOrigin = 0, fromOrigin = 0;
		string? error = null;
		bool completed = false;

		// Logs and closes the capture for the CONNECT exchange itself. Called once, either explicitly
		// (when interception takes over) or when the tunnel ends.
		void CompleteOnce() {
			if (completed) return;
			completed = true;
			var record = new ExchangeRecord {
				Timestamp = DateTimeOffset.UtcNow,
				Client = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
				Host = host,
				Port = port,
				Method = "CONNECT",
				Url = target,
				ClientHttpVersion = protocol,
				Status = status == 0 ? null : status,
				RequestHeaders = HeaderRules.ForLog(headers),
				RequestBytes = toOrigin,
				ResponseBytes = fromOrigin,
				DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				Mode = status == 200 ? mode : "rejected",
				Error = error,
			};
			log.Write(record);
			exchangeCapture.Complete(record);
		}

		try {
			using var headTimeout = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
			headTimeout.CancelAfter(TimeSpan.FromSeconds(30));
			var head = await ReadHeadAsync(input, headTimeout.Token);
			if (head is null) { error = "incomplete CONNECT request"; return; }

			(string method, target, protocol) = ParseRequestLine(head.Value.Lines[0]);
			headers = head.Value.Lines.Skip(1).Select(ParseHeader).Where(h => h.Key.Length > 0).ToList();
			exchangeCapture.WriteRequestHead(method, target, protocol, headers);

			if (!TunnelRelay.TryParseTarget(target, out host, out port)) {
				status = 400;
				error = "CONNECT target is not host:port";
				await RespondAsync(connection, exchangeCapture, status, "Expected CONNECT host:port.");
				return;
			}

			if (ShouldIntercept(host)) {
				mode = "intercept";
				status = 200;
				exchangeCapture.WriteResponseHead("HTTP/1.1 200 Connection Established", Array.Empty<KeyValuePair<string, string>>());
				await connection.Transport.Output.WriteAsync(Established);
				// The CONNECT itself is finished; the requests inside the tunnel are logged as their own exchanges.
				CompleteOnce();
				await InterceptAsync(connection, next, host, port);
				return;
			}

			TcpClient origin;
			try {
				origin = await TunnelRelay.ConnectAsync(host, port, options.ConnectTimeout, connection.ConnectionClosed);
			}
			catch (OperationCanceledException) when (!connection.ConnectionClosed.IsCancellationRequested) {
				status = 504;
				error = "timed out connecting to origin";
				await RespondAsync(connection, exchangeCapture, status, "Timed out connecting to the origin.");
				return;
			}
			catch (SocketException ex) {
				limits.Report("connecting to an origin", ex);
				status = 502;
				error = $"origin unreachable: {ex.Message}";
				await RespondAsync(connection, exchangeCapture, status, "Could not reach the origin.");
				return;
			}

			using (origin) {
				status = 200;
				exchangeCapture.WriteResponseHead("HTTP/1.1 200 Connection Established", Array.Empty<KeyValuePair<string, string>>());
				await connection.Transport.Output.WriteAsync(Established);

				// Bytes the client sent right after its request head are still in the pipe and are relayed first.
				using var client = new DuplexStream(input.AsStream(), connection.Transport.Output.AsStream());
				using var tracking = activity.Track(ActivityKind.Tunnel);
				// Not tied to ConnectionClosed: Kestrel fires that as soon as the client half-closes (stops sending),
				// but the origin's reply may still be on its way. The idle timeout and proxy shutdown end stuck tunnels.
				(toOrigin, fromOrigin) = await TunnelRelay.RelayAsync(client, origin, options.TunnelIdleTimeout, shutdown, stopping);
			}
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) {
			error ??= ex.Message;
			if (completed) Console.Error.WriteLine($"Tunnel error after the CONNECT was logged: {ex.Message}");
		}
		finally {
			CompleteOnce();
		}
	}

	bool ShouldIntercept(string host) =>
		options.Intercept && authority is not null && !options.InterceptBypass.Any(pattern => Matches(pattern, host));

	/// <summary>True when <paramref name="host"/> equals the pattern, or ends with it when the pattern starts with "*.".</summary>
	public static bool Matches(string pattern, string host) =>
		pattern.StartsWith("*.", StringComparison.Ordinal)
			? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
			: host.Equals(pattern, StringComparison.OrdinalIgnoreCase);

	enum Sniffed { Tls, PlainHttp, Other }

	static readonly string[] HttpMethodPrefixes = { "GET ", "POST ", "PUT ", "HEAD ", "DELETE ", "OPTIONS ", "PATCH ", "TRACE ", "CONNECT " };

	/// <summary>
	/// A CONNECT to an intercepted host is not always TLS (ws:// uses plain HTTP on port 80, and other protocols can be tunneled too).
	/// Look at the first bytes the client sends: a TLS handshake is decrypted, plain HTTP is handed to Kestrel as it is,
	/// and anything else (or a client that sends nothing, as with protocols where the server speaks first) is relayed blindly.
	/// </summary>
	async Task InterceptAsync(ConnectionContext connection, ConnectionDelegate next, string host, int port) {
		switch (await SniffAsync(connection.Transport.Input, options.SniffTimeout, connection.ConnectionClosed)) {
			case Sniffed.Tls:
				await InterceptTlsAsync(connection, next, host, port);
				return;
			case Sniffed.PlainHttp:
				// Already readable: just tell the HTTP layer where the requests are really going.
				connection.Items[InterceptTargetKey] = new UriBuilder(Uri.UriSchemeHttp, host, port).Uri;
				await next(connection);
				return;
			default:
				await RelayUnrecognizedAsync(connection, host, port);
				return;
		}
	}

	/// <summary>Connects to the origin and relays a tunnel whose content is neither TLS nor HTTP.</summary>
	async Task RelayUnrecognizedAsync(ConnectionContext connection, string host, int port) {
		long started = Stopwatch.GetTimestamp();
		string? error = null;
		long toOrigin = 0, fromOrigin = 0;
		try {
			using var origin = await TunnelRelay.ConnectAsync(host, port, options.ConnectTimeout, connection.ConnectionClosed);
			using var client = new DuplexStream(connection.Transport.Input.AsStream(), connection.Transport.Output.AsStream());
			using var tracking = activity.Track(ActivityKind.Tunnel);
			(toOrigin, fromOrigin) = await TunnelRelay.RelayAsync(client, origin, options.TunnelIdleTimeout, shutdown, stopping);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) {
			limits.Report("connecting to an origin", ex);
			error = $"origin unreachable: {ex.Message}";
		}
		log.Write(new ExchangeRecord {
			Timestamp = DateTimeOffset.UtcNow,
			Client = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
			Host = host,
			Port = port,
			Method = "CONNECT",
			Url = $"{host}:{port}",
			ClientHttpVersion = "HTTP/1.1",
			Status = error is null ? 200 : null,
			RequestBytes = toOrigin,
			ResponseBytes = fromOrigin,
			DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
			Mode = error is null ? "tunnel" : "rejected",
			Error = error,
		});
	}

	/// <summary>Looks at the first bytes of a tunnel without consuming them. Waits only briefly: a client that stays silent is treated as "other".</summary>
	static async Task<Sniffed> SniffAsync(PipeReader input, TimeSpan timeout, CancellationToken connectionClosed) {
		using var timer = CancellationTokenSource.CreateLinkedTokenSource(connectionClosed);
		timer.CancelAfter(timeout);
		try {
			while (true) {
				var result = await input.ReadAsync(timer.Token);
				var buffer = result.Buffer;
				var kind = Classify(buffer.Slice(0, Math.Min(buffer.Length, 9)).ToArray());
				if (kind is not null || result.IsCompleted) {
					input.AdvanceTo(buffer.Start); // consume and examine nothing: the real reader sees the same bytes
					return kind ?? Sniffed.Other;
				}
				input.AdvanceTo(buffer.Start, buffer.End); // too few bytes to tell yet
			}
		}
		catch (OperationCanceledException) when (!connectionClosed.IsCancellationRequested) {
			return Sniffed.Other; // the client said nothing within the time limit
		}
	}

	/// <summary>Null means "not enough bytes to decide yet".</summary>
	static Sniffed? Classify(byte[] firstBytes) {
		if (firstBytes.Length == 0) return null;
		// A TLS handshake record starts with 0x16 (handshake) 0x03 (major version).
		if (firstBytes[0] == 0x16) {
			if (firstBytes.Length < 2) return null;
			return firstBytes[1] == 0x03 ? Sniffed.Tls : Sniffed.Other;
		}
		var text = Encoding.ASCII.GetString(firstBytes);
		if (HttpMethodPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal))) return Sniffed.PlainHttp;
		// Could still turn into a method name once more bytes arrive.
		if (HttpMethodPrefixes.Any(prefix => prefix.StartsWith(text, StringComparison.Ordinal))) return null;
		return Sniffed.Other;
	}

	/// <summary>
	/// Does the TLS handshake with the client (presenting a certificate generated for the host), then replaces the
	/// connection's transport with the decrypted stream and lets Kestrel process it as HTTP/1.1 or HTTP/2, whichever
	/// the client chose during the handshake (ALPN).
	/// </summary>
	async Task InterceptTlsAsync(ConnectionContext connection, ConnectionDelegate next, string host, int port) {
		var clientSide = new DuplexStream(connection.Transport.Input.AsStream(), connection.Transport.Output.AsStream());
		await using var tls = new SslStream(clientSide, leaveInnerStreamOpen: false);

		var protocols = new List<SslApplicationProtocol>();
		if (options.EnableHttp2) protocols.Add(SslApplicationProtocol.Http2);
		protocols.Add(SslApplicationProtocol.Http11);

		try {
			using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
			handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(15));
			await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
				ServerCertificateSelectionCallback = (_, serverName) => authority!.GetCertificateFor(ChooseName(serverName, host)),
				ApplicationProtocols = protocols,
				ClientCertificateRequired = false,
			}, handshakeTimeout.Token);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) {
			LogTlsFailure(connection, host, port, ex);
			return;
		}

		connection.Items[InterceptTargetKey] = new UriBuilder(Uri.UriSchemeHttps, host, port).Uri;
		// Kestrel only picks HTTP/2 for a connection that looks like TLS and reports "h2" as the negotiated protocol,
		// so tell it what the handshake above agreed on.
		connection.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature());
		connection.Features.Set<ITlsApplicationProtocolFeature>(new TlsApplicationProtocolFeature(tls.NegotiatedApplicationProtocol.Protocol));
		// With TLS 1.3 the server can finish its side of the handshake before the client has accepted the certificate,
		// so a client that does not trust the MoSaProxy root only shows up as an error on the first read. Watch for that.
		var observed = new ObservedStream(tls);
		connection.Transport = new DuplexPipe(PipeReader.Create(observed), PipeWriter.Create(observed));
		await next(connection);
		if (observed.BytesRead == 0 && observed.ReadFailure is { } failure)
			LogTlsFailure(connection, host, port, failure);
	}

	void LogTlsFailure(ConnectionContext connection, string host, int port, Exception ex) =>
		// Usually the client does not trust the MoSaProxy root certificate (or pins the site's certificate).
		log.Write(new ExchangeRecord {
			Timestamp = DateTimeOffset.UtcNow,
			Client = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
			Host = host,
			Port = port,
			Method = "CONNECT",
			Url = $"{host}:{port}",
			ClientHttpVersion = "HTTP/1.1",
			Mode = "rejected",
			Error = $"TLS handshake with client failed (does it trust the MoSaProxy root CA?): {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}",
		});

	/// <summary>The name to issue a certificate for: what the client asked for in SNI, falling back to the CONNECT host.</summary>
	static string ChooseName(string? serverName, string connectHost) =>
		!string.IsNullOrWhiteSpace(serverName) && Uri.CheckHostName(serverName) != UriHostNameType.Unknown ? serverName : connectHost;

	/// <summary>Looks at the first bytes of the connection without consuming them.</summary>
	static async Task<bool> StartsWithConnectAsync(PipeReader input, CancellationToken cancellationToken) {
		while (true) {
			var result = await input.ReadAsync(cancellationToken);
			var buffer = result.Buffer;
			bool enough = buffer.Length >= ConnectPrefix.Length;
			bool matches = false;
			if (enough || result.IsCompleted) {
				var prefix = buffer.Slice(0, Math.Min(buffer.Length, ConnectPrefix.Length)).ToArray();
				matches = enough && prefix.AsSpan().SequenceEqual(ConnectPrefix);
			}
			if (enough || result.IsCompleted) {
				// Consume and examine nothing, so the next reader sees the same bytes immediately.
				input.AdvanceTo(buffer.Start);
				return matches;
			}
			input.AdvanceTo(buffer.Start, buffer.End); // need more bytes before we can decide
		}
	}

	/// <summary>Reads up to the blank line that ends the request head and consumes exactly that much.</summary>
	static async Task<(string[] Lines, int Length)?> ReadHeadAsync(PipeReader input, CancellationToken cancellationToken) {
		while (true) {
			var result = await input.ReadAsync(cancellationToken);
			var buffer = result.Buffer;
			var reader = new SequenceReader<byte>(buffer);
			if (reader.TryReadTo(out ReadOnlySequence<byte> headBytes, "\r\n\r\n"u8, advancePastDelimiter: true)) {
				string text = Encoding.Latin1.GetString(headBytes);
				input.AdvanceTo(reader.Position);
				return (text.Split("\r\n"), (int)headBytes.Length);
			}
			if (buffer.Length > MaxHeadBytes || result.IsCompleted) {
				input.AdvanceTo(buffer.End);
				return null;
			}
			input.AdvanceTo(buffer.Start, buffer.End);
		}
	}

	static (string Method, string Target, string Protocol) ParseRequestLine(string line) {
		var parts = line.Split(' ', 3);
		return (parts[0], parts.Length > 1 ? parts[1] : "", parts.Length > 2 ? parts[2] : "HTTP/1.1");
	}

	static KeyValuePair<string, string> ParseHeader(string line) {
		int colon = line.IndexOf(':');
		return colon > 0 ? KeyValuePair.Create(line[..colon].Trim(), line[(colon + 1)..].Trim()) : default;
	}

	static async Task RespondAsync(ConnectionContext connection, IExchangeCapture capture, int status, string message) {
		var body = Encoding.UTF8.GetBytes(message);
		string reason = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
		var headers = new[] {
			KeyValuePair.Create("Content-Type", "text/plain"),
			KeyValuePair.Create("Content-Length", body.Length.ToString()),
			KeyValuePair.Create("Connection", "close"),
		};
		capture.WriteResponseHead($"HTTP/1.1 {status} {reason}", headers);
		capture.WriteResponseBody(body);

		var text = new StringBuilder($"HTTP/1.1 {status} {reason}\r\n");
		foreach (var (name, value) in headers) text.Append(name).Append(": ").Append(value).Append("\r\n");
		text.Append("\r\n");
		var output = connection.Transport.Output;
		await output.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()));
		await output.WriteAsync(body);
		await output.FlushAsync();
	}
}

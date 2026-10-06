using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace mos;

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
public sealed class TunnelHandler(
	IExchangeLog log, ISessionCapture capture, ProxyOptions options, CancellationToken shutdown, CertificateAuthority? authority = null) {

	/// <summary>Key in the connection's Items holding the https:// base URI of an intercepted connection.</summary>
	public const string InterceptTargetKey = "mos.intercept-target";

	const int MaxHeadBytes = 64 * 1024;
	static readonly byte[] ConnectPrefix = Encoding.ASCII.GetBytes("CONNECT ");
	static readonly byte[] Established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");

	public async Task HandleAsync(ConnectionContext connection, ConnectionDelegate next) {
		var input = connection.Transport.Input;

		if (!await StartsWithConnectAsync(input, connection.ConnectionClosed)) {
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
				// Not tied to ConnectionClosed: Kestrel fires that as soon as the client half-closes (stops sending),
				// but the origin's reply may still be on its way. The idle timeout and proxy shutdown end stuck tunnels.
				(toOrigin, fromOrigin) = await TunnelRelay.RelayAsync(client, origin, options.TunnelIdleTimeout, shutdown);
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

	/// <summary>
	/// Does the TLS handshake with the client (presenting a certificate generated for the host), then replaces the
	/// connection's transport with the decrypted stream and lets Kestrel process it as plain HTTP/1.1.
	/// </summary>
	async Task InterceptAsync(ConnectionContext connection, ConnectionDelegate next, string host, int port) {
		var clientSide = new DuplexStream(connection.Transport.Input.AsStream(), connection.Transport.Output.AsStream());
		await using var tls = new SslStream(clientSide, leaveInnerStreamOpen: false);

		try {
			using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
			handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(15));
			await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
				ServerCertificateSelectionCallback = (_, serverName) => authority!.GetCertificateFor(ChooseName(serverName, host)),
				ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 },
				ClientCertificateRequired = false,
			}, handshakeTimeout.Token);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) {
			LogTlsFailure(connection, host, port, ex);
			return;
		}

		connection.Items[InterceptTargetKey] = new UriBuilder(Uri.UriSchemeHttps, host, port).Uri;
		// With TLS 1.3 the server can finish its side of the handshake before the client has accepted the certificate,
		// so a client that does not trust the mos root only shows up as an error on the first read. Watch for that.
		var observed = new ObservedStream(tls);
		connection.Transport = new DuplexPipe(PipeReader.Create(observed), PipeWriter.Create(observed));
		await next(connection);
		if (observed.BytesRead == 0 && observed.ReadFailure is { } failure)
			LogTlsFailure(connection, host, port, failure);
	}

	void LogTlsFailure(ConnectionContext connection, string host, int port, Exception ex) =>
		// Usually the client does not trust the mos root certificate (or pins the site's certificate).
		log.Write(new ExchangeRecord {
			Timestamp = DateTimeOffset.UtcNow,
			Client = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
			Host = host,
			Port = port,
			Method = "CONNECT",
			Url = $"{host}:{port}",
			ClientHttpVersion = "HTTP/1.1",
			Mode = "rejected",
			Error = $"TLS handshake with client failed (does it trust the mos root CA?): {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}",
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

/// <summary>Combines a read-only stream and a write-only stream into one duplex stream.</summary>
sealed class DuplexStream(Stream readSide, Stream writeSide) : Stream {
	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => readSide.ReadAsync(buffer, cancellationToken);
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		await writeSide.WriteAsync(buffer, cancellationToken);
		await writeSide.FlushAsync(cancellationToken);
	}
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>A simple IDuplexPipe made of one reader and one writer.</summary>
sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe {
	public PipeReader Input => input;
	public PipeWriter Output => output;
}

/// <summary>Passes everything through to <c>inner</c>, remembering how much was read and the first read failure (if any).</summary>
sealed class ObservedStream(Stream inner) : Stream {
	public long BytesRead { get; private set; }
	public Exception? ReadFailure { get; private set; }

	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => false;
	public override bool CanWrite => inner.CanWrite;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		try {
			int n = await inner.ReadAsync(buffer, cancellationToken);
			BytesRead += n;
			return n;
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			ReadFailure ??= ex;
			throw;
		}
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
	public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync.");
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync.");
	public override void Flush() => inner.Flush();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
	public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); await base.DisposeAsync(); }
}

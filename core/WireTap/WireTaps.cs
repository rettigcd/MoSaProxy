using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace MoSaProxy;

/// <summary>
/// Records the bytes that really cross the wire on HTTP/1.x and HTTP/2 connections: what each client sends the proxy, and what each
/// origin sends it, so the capture files can hold the exact bytes (HTTP/1.x) or the decoded frames (HTTP/2) instead of a reconstruction
/// of what Kestrel parsed. It sits in two places: a connection middleware after the tunnel handler (so it sees the decrypted bytes of an
/// intercepted tunnel), and the plain-text stream filter of the proxy's own connections to origins. It also reports a request that
/// Kestrel rejected before it reached the handler, which would otherwise not be seen at all.
/// </summary>
public sealed class WireTaps(ResourceLimitMonitor limits, IExchangeLog log, ISessionCapture capture) {
	readonly ConcurrentDictionary<string, ConnectionWire> _connections = new();
	readonly ConcurrentDictionary<Http2OriginWire, byte> _http2Origins = new();
	int _originConnections;

	/// <summary>The recording of one client connection, or null if it is not being recorded.</summary>
	public ConnectionWire? ForConnection(string connectionId) => _connections.TryGetValue(connectionId, out var wire) ? wire : null;

	internal async Task TapConnectionAsync(ConnectionContext connection, ConnectionDelegate next) {
		var wire = new ConnectionWire(limits);
		_connections[connection.ConnectionId] = wire;
		connection.Transport = new TapPipe(new TapReader(connection.Transport.Input, wire), new TapWriter(connection.Transport.Output, wire));
		try { await next(connection); }
		finally {
			_connections.TryRemove(connection.ConnectionId, out _);
			ReportUnansweredRequest(connection, wire);
		}
	}

	static bool LooksLikeRequestLine(string line) => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z]+ \S+ HTTP/\d(\.\d)?$");

	/// <summary>
	/// A request Kestrel refused (malformed, too large...) or that the client abandoned part way never reaches the handler, so it would
	/// leave no trace. When the connection ends with such bytes left over, record them as an exchange of their own.
	/// </summary>
	void ReportUnansweredRequest(ConnectionContext connection, ConnectionWire wire) {
		try {
			if (wire.TakeUnansweredRequest() is not { } left) return;
			// Only a request line (METHOD target HTTP/x) is read for the method and URL. Anything else (a client speaking another
			// protocol through a tunnel) is kept as bytes in the capture files, but not as text in the record.
			string requestLine = Encoding.Latin1.GetString(left.Request, 0, Math.Min(left.Request.Length, 8192)).Split("\r\n")[0];
			if (!LooksLikeRequestLine(requestLine)) requestLine = "";
			var parts = requestLine.Split(' ');
			string method = parts.Length > 0 ? parts[0] : "";
			string target = parts.Length > 1 ? parts[1] : "";
			string protocol = parts.Length > 2 ? parts[2] : "";
			Uri.TryCreate(target, UriKind.Absolute, out var uri);
			int? status = null;
			if (left.Response is { } response && Encoding.Latin1.GetString(response).Split(' ') is { Length: > 1 } words && int.TryParse(words[1], out int code)) status = code;

			var record = new ExchangeRecord {
				Timestamp = DateTimeOffset.UtcNow,
				Client = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
				Scheme = uri?.Scheme ?? "",
				Host = uri?.Host ?? "",
				Port = uri?.Port ?? 0,
				Method = method,
				Url = uri?.ToString() ?? target,
				ClientHttpVersion = protocol,
				Status = status,
				RequestBytes = left.Request.Length,
				ResponseBytes = left.Response?.Length ?? 0,
				Mode = "rejected",
				Error = status is null ? "the connection ended with an incomplete request" : "rejected before it reached the proxy's request handler",
			};
			var exchange = capture.BeginExchange();
			exchange.SetWireBytes(left.Request, left.Response);
			log.Write(record);
			exchange.Complete(record);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException) {
			Console.Error.WriteLine($"Could not record a rejected request: {ex.Message}");
		}
	}

	/// <summary>Wraps the plain-text stream of a connection to an origin, whether it carries HTTP/1.x or HTTP/2.</summary>
	internal Stream TapOrigin(SocketsHttpPlaintextStreamFilterContext context) {
		if (context.NegotiatedHttpVersion.Major == 1) return new OriginTapStream(context.PlaintextStream, new OriginWire(limits, Interlocked.Increment(ref _originConnections)));
		var wire = new Http2OriginWire(limits);
		_http2Origins[wire] = 0;
		return new OriginTapStream(context.PlaintextStream, wire);
	}

	/// <summary>Finds an HTTP/2 origin connection that carried this request and takes its response as HTTP/1.1-style text, or null.</summary>
	internal byte[]? TakeHttp2Response(string method, string authority, string pathAndQuery) {
		foreach (var (wire, _) in _http2Origins) {
			if (wire.TakeResponse(method, authority, pathAndQuery) is { } response) return response;
			// A connection that has been closed for a while and holds nothing, or nobody came for what it holds, is forgotten.
			if (wire.ClosedAt is { } closed && (wire.IsEmpty || DateTime.UtcNow - closed > TimeSpan.FromMinutes(1))) _http2Origins.TryRemove(wire, out _);
		}
		return null;
	}
}

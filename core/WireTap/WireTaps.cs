using System.Collections.Concurrent;
using Microsoft.AspNetCore.Connections;

namespace MoSaProxy;

/// <summary>
/// Records the bytes that really cross the wire on HTTP/1.x connections: what each client sends the proxy, and what each origin
/// sends it, so the capture files can hold the exact bytes instead of a reconstruction of what Kestrel parsed. It sits in two places:
/// a connection middleware after the tunnel handler (so it sees the decrypted bytes of an intercepted tunnel), and the plain-text
/// stream filter of the proxy's own connections to origins.
/// </summary>
public sealed class WireTaps(ResourceLimitMonitor limits) {
	readonly ConcurrentDictionary<string, ConnectionWire> _connections = new();

	/// <summary>The recording of one client connection, or null if it is not being recorded.</summary>
	public ConnectionWire? ForConnection(string connectionId) => _connections.TryGetValue(connectionId, out var wire) ? wire : null;

	internal async Task TapConnectionAsync(ConnectionContext connection, ConnectionDelegate next) {
		var wire = new ConnectionWire(limits);
		_connections[connection.ConnectionId] = wire;
		connection.Transport = new TapPipe(new TapReader(connection.Transport.Input, wire), connection.Transport.Output);
		try { await next(connection); }
		finally { _connections.TryRemove(connection.ConnectionId, out _); }
	}

	readonly ConcurrentDictionary<Http2OriginWire, byte> _http2Origins = new();

	/// <summary>Wraps the plain-text stream of a connection to an origin, whether it carries HTTP/1.x or HTTP/2.</summary>
	internal Stream TapOrigin(SocketsHttpPlaintextStreamFilterContext context) {
		if (context.NegotiatedHttpVersion.Major == 1) return new OriginTapStream(context.PlaintextStream, new OriginWire(limits));
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

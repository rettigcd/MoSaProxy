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

	/// <summary>Wraps the plain-text stream of a connection to an origin. HTTP/2 connections are left alone: they are not HTTP/1.x text.</summary>
	internal Stream TapOrigin(SocketsHttpPlaintextStreamFilterContext context) =>
		context.NegotiatedHttpVersion.Major == 1 ? new OriginTapStream(context.PlaintextStream, new OriginWire(limits)) : context.PlaintextStream;
}

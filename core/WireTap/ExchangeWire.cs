namespace MoSaProxy;

/// <summary>
/// Ties one exchange to the origin connection it was sent on, so that the response can be taken from that connection's bytes.
/// The current exchange is carried in an <see cref="AsyncLocal{T}"/>: <c>SocketsHttpHandler</c> writes an HTTP/1.1 request on the
/// caller's own async flow, so the tap sees it at the moment the request goes out.
/// </summary>
public sealed class ExchangeWire(WireTaps taps, string method, string authority, string pathAndQuery) {
	internal static readonly AsyncLocal<ExchangeWire?> Current = new();

	internal string Method { get; } = method;
	internal OriginWire? Origin { get; set; }
	internal int ResponseIndex { get; set; }

	/// <summary>Makes this the exchange whose request is about to be written to an origin, for the rest of the calling async flow.</summary>
	public void MakeCurrent() => Current.Value = this;

	/// <summary>
	/// The origin's response and where it came from: the exact bytes of an HTTP/1.x connection (or what arrived of them), or, for an HTTP/2
	/// connection, the response decoded from its frames. Null if the exchange did not use a recorded connection.
	/// </summary>
	public (byte[] Bytes, string Source)? TakeResponse() {
		if (Origin is not null) return Origin.TakeResponse(ResponseIndex) is { } bytes ? (bytes, CaptureSource.Wire) : null;
		return taps.TakeHttp2Response(method, authority, pathAndQuery) is { } decoded ? (decoded, CaptureSource.Http2Decoded) : null;
	}
}

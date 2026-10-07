namespace MoSaProxy;

/// <summary>
/// Ties one exchange to the origin connection it was sent on, so that the response can be taken from that connection's bytes.
/// The current exchange is carried in an <see cref="AsyncLocal{T}"/>: <c>SocketsHttpHandler</c> writes an HTTP/1.1 request on the
/// caller's own async flow, so the tap sees it at the moment the request goes out.
/// </summary>
public sealed class ExchangeWire(string method) {
	internal static readonly AsyncLocal<ExchangeWire?> Current = new();

	internal string Method { get; } = method;
	internal OriginWire? Origin { get; set; }
	internal int ResponseIndex { get; set; }

	/// <summary>Makes this the exchange whose request is about to be written to an origin, for the rest of the calling async flow.</summary>
	public void MakeCurrent() => Current.Value = this;

	/// <summary>The origin's response exactly as it arrived (or what arrived of it), or null if the exchange did not use a recorded HTTP/1.x connection.</summary>
	public byte[]? TakeResponse() => Origin?.TakeResponse(ResponseIndex);
}

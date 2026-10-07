namespace MoSaProxy;

/// <summary>Where the content of a capture file came from, as recorded in the exchange's metadata (<c>RequestSource</c> and <c>ResponseSource</c>).</summary>
public static class CaptureSource {
	/// <summary>The exact bytes that crossed the wire (HTTP/1.x).</summary>
	public const string Wire = "wire";

	/// <summary>Decoded from HTTP/2 frames, with the headers in the order they were sent, and written as HTTP/1.1-style text. Not produced yet.</summary>
	public const string Http2Decoded = "http2-decoded";

	/// <summary>Written from headers and bodies that Kestrel or HttpClient had already parsed.</summary>
	public const string Parsed = "parsed";

	/// <summary>Written by the proxy itself, such as its own error pages and the reply to CONNECT.</summary>
	public const string Generated = "generated";
}

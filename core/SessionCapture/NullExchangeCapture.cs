namespace MoSaProxy;

/// <summary>
/// Capture of one exchange. Implementations must never throw: a capture problem must not break forwarding.
/// </summary>
public interface IExchangeCapture {
	int Number { get; }
	void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the request file.</summary>
	Stream TeeRequestBody(Stream source);
	/// <param name="generated">True when the proxy made this response itself (an error page, the CONNECT reply) instead of relaying an origin's.</param>
	void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers, bool generated = false);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the response file.</summary>
	Stream TeeResponseBody(Stream source);
	void WriteResponseBody(ReadOnlySpan<byte> body);
	/// <summary>
	/// Records one WebSocket message (or close frame) in the exchange's N_w.txt file. For a message that is longer than
	/// the capture limit, <paramref name="payload"/> is the part that was kept and <paramref name="totalBytes"/> the full size.
	/// </summary>
	void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null);
	/// <summary>
	/// Supplies the exact bytes the client sent and the origin answered, when they are known (HTTP/1.x). They replace the
	/// request and response files that were built from parsed data. Call before <see cref="Complete"/>; either may be null.
	/// </summary>
	void SetWireBytes(byte[]? request, byte[]? response);
	/// <summary>
	/// Says that <see cref="SetWireBytes"/> will supply the request and/or the response, so the file need not be written while the
	/// exchange runs (it would only be overwritten). Call before the matching head is written. If the bytes then do not arrive, the
	/// file keeps the head only and the metadata says the body is missing.
	/// </summary>
	void ExpectWireBytes(bool request = false, bool response = false);
	/// <summary>Closes the files and writes the metadata file.</summary>
	void Complete(ExchangeRecord record);
}

public sealed class NullExchangeCapture : IExchangeCapture {
	public static readonly NullExchangeCapture Instance = new();
	public int Number => 0;
	public void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers) { }
	public Stream TeeRequestBody(Stream source) => source;
	public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers, bool generated = false) { }
	public Stream TeeResponseBody(Stream source) => source;
	public void WriteResponseBody(ReadOnlySpan<byte> body) { }
	public void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null) { }
	public void SetWireBytes(byte[]? request, byte[]? response) { }
	public void ExpectWireBytes(bool request = false, bool response = false) { }
	public void Complete(ExchangeRecord record) { }
}

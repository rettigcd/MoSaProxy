namespace MoSaProxy;

/// <summary>
/// Capture of one exchange. Implementations must never throw: a capture problem must not break forwarding.
/// </summary>
public interface IExchangeCapture {
	int Number { get; }
	void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the request file.</summary>
	Stream TeeRequestBody(Stream source);
	void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the response file.</summary>
	Stream TeeResponseBody(Stream source);
	void WriteResponseBody(ReadOnlySpan<byte> body);
	/// <summary>
	/// Records one WebSocket message (or close frame) in the exchange's N_w.txt file. For a message that is longer than
	/// the capture limit, <paramref name="payload"/> is the part that was kept and <paramref name="totalBytes"/> the full size.
	/// </summary>
	void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null);
	/// <summary>Closes the files and writes the metadata file.</summary>
	void Complete(ExchangeRecord record);
}

public sealed class NullExchangeCapture : IExchangeCapture {
	public static readonly NullExchangeCapture Instance = new();
	public int Number => 0;
	public void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers) { }
	public Stream TeeRequestBody(Stream source) => source;
	public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers) { }
	public Stream TeeResponseBody(Stream source) => source;
	public void WriteResponseBody(ReadOnlySpan<byte> body) { }
	public void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null) { }
	public void Complete(ExchangeRecord record) { }
}

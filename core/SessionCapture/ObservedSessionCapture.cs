namespace MoSaProxy;

/// <summary>Passes everything to another capture and announces, through <see cref="CaptureEvents"/>, when exchanges start and end and when WebSocket messages pass.</summary>
public sealed class ObservedSessionCapture(ISessionCapture inner, CaptureEvents events) : ISessionCapture {
	public IExchangeCapture BeginExchange() {
		var exchange = inner.BeginExchange();
		events.RaiseStarted(exchange.Number);
		return new Observed(exchange, events);
	}

	sealed class Observed(IExchangeCapture inner, CaptureEvents events) : IExchangeCapture {
		public int Number => inner.Number;
		public void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers) => inner.WriteRequestHead(method, target, protocol, headers);
		public Stream TeeRequestBody(Stream source) => inner.TeeRequestBody(source);
		public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers, bool generated = false) => inner.WriteResponseHead(statusLine, headers, generated);
		public Stream TeeResponseBody(Stream source) => inner.TeeResponseBody(source);
		public void WriteResponseBody(ReadOnlySpan<byte> body) => inner.WriteResponseBody(body);
		public void SetWireBytes(byte[]? request, byte[]? response, string requestSource = CaptureSource.Wire, string responseSource = CaptureSource.Wire) => inner.SetWireBytes(request, response, requestSource, responseSource);
		public void ExpectWireBytes(bool request = false, bool response = false) => inner.ExpectWireBytes(request, response);

		public void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null) {
			inner.WriteWebSocketMessage(fromClient, type, payload, totalBytes, note);
			events.RaiseWebSocketMessage(new WebSocketMessageInfo(Number, fromClient, type, totalBytes));
		}

		public void Complete(ExchangeRecord record) {
			inner.Complete(record);   // writes the metadata file, which is what makes the exchange visible in the store
			events.RaiseFinished(Number);
		}
	}
}

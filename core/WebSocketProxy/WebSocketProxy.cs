using System.Buffers;
using System.Net.WebSockets;

namespace MoSaProxy;

/// <summary>
/// Passes WebSocket connections through: it opens a WebSocket to the origin first (so the origin's choice of subprotocol is known),
/// then accepts the client's WebSocket and copies messages between the two. Works for HTTP/1.1 upgrades and for
/// WebSockets over HTTP/2. The two connections are independent, so ping/pong and compression are handled separately on each side.
/// </summary>
/// <param name="stopping">Signalled when the proxy starts shutting down: both legs of every open connection are sent a close (1001, going away).</param>
/// <param name="shutdown">Signalled when the drain period is over: connections that have not finished closing are aborted.</param>
public sealed class WebSocketProxy(HttpMessageInvoker upstream, ProxyOptions options, ResourceLimitMonitor limits, CancellationToken stopping, CancellationToken shutdown) {

	const int BufferSize = 64 * 1024;

	// Headers that belong to one connection or to the handshake itself; the origin handshake creates its own.
	static readonly HashSet<string> NotForwarded = new(StringComparer.OrdinalIgnoreCase) {
		"Host", "Upgrade", "Connection", "Sec-WebSocket-Key", "Sec-WebSocket-Version", "Sec-WebSocket-Extensions",
		"Sec-WebSocket-Protocol", "Sec-WebSocket-Accept", "Content-Length",
	};

	/// <param name="httpUri">The http:// or https:// form of the address being opened (it is opened as ws:// or wss://).</param>
	/// <param name="onConnected">Called once the handshake with both sides has succeeded, before any message is copied.</param>
	public async Task<WebSocketSummary> RunAsync(HttpContext context, IExchangeCapture capture, Uri httpUri, Action onConnected) {
		var request = context.Request;
		var wsUri = new UriBuilder(httpUri) { Scheme = httpUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" }.Uri;

		using var origin = new ClientWebSocket();
		origin.Options.CollectHttpResponseDetails = true;
		origin.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
		foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols) origin.Options.AddSubProtocol(protocol);
		var hopByHop = HeaderRules.HeadersToDrop(request.Headers);
		foreach (var (name, values) in request.Headers) {
			if (hopByHop.Contains(name) || NotForwarded.Contains(name)) continue;
			try { origin.Options.SetRequestHeader(name, values.ToString()); }
			catch (ArgumentException) { /* a header the WebSocket client does not allow to be set */ }
		}

		try {
			using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, shutdown);
			connectTimeout.CancelAfter(options.UpstreamTimeout);
			await origin.ConnectAsync(wsUri, upstream, connectTimeout.Token);
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
			return Failed(0, "client disconnected");
		}
		catch (OperationCanceledException) {
			return await RejectAsync(context, capture, 504, "origin did not answer the WebSocket handshake in time");
		}
		catch (Exception ex) when (ex is WebSocketException or HttpRequestException) {
			limits.Report("connecting a WebSocket to an origin", ex);
			// If the origin answered with an HTTP status (for example 403 or 401), pass that on; otherwise it was unreachable.
			int status = (int)origin.HttpStatusCode;
			if (status is < 300 or > 599) status = 502;
			return await RejectAsync(context, capture, status, $"WebSocket handshake with the origin failed: {ex.InnerException?.Message ?? ex.Message}");
		}

		capture.WriteResponseHead("HTTP/1.1 101 Switching Protocols",
			(origin.HttpResponseHeaders ?? new Dictionary<string, IEnumerable<string>>()).SelectMany(h => h.Value.Select(v => KeyValuePair.Create(h.Key, v))));

		using var client = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext {
			SubProtocol = origin.SubProtocol,
			KeepAliveInterval = Timeout.InfiniteTimeSpan,
		});
		onConnected();

		// Shutdown: tell both ends the connection is going away, then let the normal close handshake finish.
		using var closeOnStopping = stopping.Register(() => _ = CloseBothAsync(client, origin));

		var totals = new Totals();
		using var done = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, shutdown);
		await Task.WhenAll(
			PumpAsync(client, origin, fromClient: true, capture, totals, done),
			PumpAsync(origin, client, fromClient: false, capture, totals, done));

		return new WebSocketSummary(101, totals.ClientBytes, totals.OriginBytes, totals.ClientMessages, totals.OriginMessages, totals.Error);
	}

	static WebSocketSummary Failed(int status, string error) => new(status, 0, 0, 0, 0, error);

	static async Task CloseBothAsync(WebSocket client, WebSocket origin) =>
		await Task.WhenAll(CloseOutputQuietlyAsync(client), CloseOutputQuietlyAsync(origin));

	static async Task CloseOutputQuietlyAsync(WebSocket socket) {
		try {
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
				await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "the proxy is shutting down", CancellationToken.None);
		}
		catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException) {
			// the connection ended on its own while we were closing it
		}
	}

	static async Task<WebSocketSummary> RejectAsync(HttpContext context, IExchangeCapture capture, int status, string error) {
		if (!context.Response.HasStarted) {
			context.Response.StatusCode = status;
			context.Response.ContentType = "text/plain";
			capture.WriteResponseHead($"HTTP/1.1 {status} {Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status)}", new[] { KeyValuePair.Create("Content-Type", "text/plain") }, generated: true);
			try { await context.Response.WriteAsync(error); } catch { }
		}
		return Failed(status, error);
	}

	/// <summary>Copies messages from one WebSocket to the other until the sender closes, then passes the close on.</summary>
	async Task PumpAsync(WebSocket from, WebSocket to, bool fromClient, IExchangeCapture capture, Totals totals, CancellationTokenSource done) {
		var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
		var kept = new MemoryStream();                 // the start of the current message, for the capture file
		long messageBytes = 0;
		long keepLimit = options.CaptureMaxBodyBytes ?? 1_048_576;
		try {
			while (!done.IsCancellationRequested) {
				var received = await from.ReceiveAsync(buffer.AsMemory(0, BufferSize), done.Token);

				if (received.MessageType == WebSocketMessageType.Close) {
					string description = from.CloseStatusDescription ?? "";
					capture.WriteWebSocketMessage(fromClient, WebSocketMessageType.Close, System.Text.Encoding.UTF8.GetBytes(description), description.Length,
						note: $"status {(from.CloseStatus is { } s ? (int)s : 1005)}");
					try {
						// If this side was already closed because the proxy is stopping, the peer's reply needs no forwarding.
						if (to.State is WebSocketState.Open or WebSocketState.CloseReceived)
							await to.CloseOutputAsync(SendableCloseStatus(from.CloseStatus), description, done.Token);
					}
					catch (WebSocketException) when (stopping.IsCancellationRequested) { }
					return;
				}

				await to.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, done.Token);
				totals.AddBytes(fromClient, received.Count);
				messageBytes += received.Count;
				if (kept.Length < keepLimit) kept.Write(buffer, 0, (int)Math.Min(received.Count, keepLimit - kept.Length));
				if (received.EndOfMessage) {
					totals.AddMessage(fromClient);
					capture.WriteWebSocketMessage(fromClient, received.MessageType, kept.GetBuffer().AsSpan(0, (int)kept.Length), messageBytes);
					kept.SetLength(0);
					messageBytes = 0;
				}
			}
		}
		catch (OperationCanceledException) {
			// the connection is being closed from the other direction, or the proxy is stopping
		}
		catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException) {
			// the peer vanished without closing: take the other direction down too
			totals.SetError($"{(fromClient ? "client" : "origin")} connection ended abruptly: {ex.Message}");
			done.Cancel();
			from.Abort();
			to.Abort();
		}
		finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	/// <summary>Close codes 1005, 1006 and 1015 describe a missing or failed close and must never be sent in a close frame.</summary>
	static WebSocketCloseStatus SendableCloseStatus(WebSocketCloseStatus? status) => status switch {
		null => WebSocketCloseStatus.NormalClosure,
		WebSocketCloseStatus.Empty => WebSocketCloseStatus.NormalClosure,
		(WebSocketCloseStatus)1006 or (WebSocketCloseStatus)1015 => WebSocketCloseStatus.NormalClosure,
		_ => status.Value,
	};

	/// <summary>Counts for the two directions. Each field is written by one direction only, and read after both have finished.</summary>
	sealed class Totals {
		string? _error;
		public long ClientBytes { get; private set; }
		public long OriginBytes { get; private set; }
		public int ClientMessages { get; private set; }
		public int OriginMessages { get; private set; }
		public string? Error => _error;

		public void AddBytes(bool fromClient, long count) { if (fromClient) ClientBytes += count; else OriginBytes += count; }
		public void AddMessage(bool fromClient) { if (fromClient) ClientMessages++; else OriginMessages++; }
		public void SetError(string error) => Interlocked.CompareExchange(ref _error, error, null);
	}
}

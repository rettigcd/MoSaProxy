namespace MoSaProxy;

/// <summary>
/// What a front end can watch while exchanges are captured. Only captured exchanges are announced, so the events match what
/// <see cref="ExchangeStore"/> can show. A handler that throws is ignored: it must not be able to break the proxy.
/// Handlers run on the proxy's own threads, so they should return quickly.
/// </summary>
public sealed class CaptureEvents {
	/// <summary>An exchange began and is being captured. It is not in the store yet.</summary>
	public event Action<int>? ExchangeStarted;

	/// <summary>An exchange ended and its metadata is written, so the store can show it.</summary>
	public event Action<int>? ExchangeFinished;

	/// <summary>A WebSocket message (or close frame) passed in a captured exchange.</summary>
	public event Action<WebSocketMessageInfo>? WebSocketMessage;

	/// <summary>Capture was switched on (true) or off (false).</summary>
	public event Action<bool>? CaptureEnabledChanged;

	internal void RaiseStarted(int number) => Raise(ExchangeStarted, number);
	internal void RaiseFinished(int number) => Raise(ExchangeFinished, number);
	internal void RaiseWebSocketMessage(WebSocketMessageInfo message) => Raise(WebSocketMessage, message);
	internal void RaiseCaptureEnabledChanged(bool enabled) => Raise(CaptureEnabledChanged, enabled);

	/// <summary>Calls every handler, each on its own, so one that throws neither breaks the proxy nor stops the ones after it.</summary>
	static void Raise<T>(Action<T>? handlers, T argument) {
		if (handlers is null) return;
		foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>()) {
			try { handler(argument); }
			catch (Exception) { }
		}
	}
}

/// <summary>One WebSocket message as announced by <see cref="CaptureEvents.WebSocketMessage"/>.</summary>
public sealed record WebSocketMessageInfo(int Number, bool FromClient, System.Net.WebSockets.WebSocketMessageType Type, long TotalBytes);

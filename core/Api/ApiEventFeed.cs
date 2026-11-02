using System.Text.Json;

namespace MoSaProxy;

/// <summary>One event as the stream sends it: a number that only grows, a name, and its data as JSON.</summary>
public sealed record ApiEvent(long Id, string Name, string Json);

/// <summary>
/// Remembers the most recent capture events, numbered, so the event stream can send them to any number of readers and a reader that
/// reconnects can ask for what it missed. It keeps a limited number; a reader that was away longer is told to fetch the list again.
/// </summary>
sealed class ApiEventFeed : IDisposable {
	public const int DefaultCapacity = 10_000;

	static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	readonly int _capacity;
	readonly CaptureEvents _events;
	readonly Lock _lock = new();
	readonly List<ApiEvent> _recent = [];
	long _last;
	TaskCompletionSource _arrived = NewSignal();

	public ApiEventFeed(CaptureEvents events, int capacity = DefaultCapacity) {
		_capacity = capacity;
		_events = events;
		events.ExchangeStarted += OnStarted;
		events.ExchangeFinished += OnFinished;
		events.WebSocketMessage += OnWebSocketMessage;
		events.CaptureEnabledChanged += OnCaptureChanged;
	}

	static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	void OnStarted(int id) => Add("exchange-started", new { id });
	void OnFinished(int id) => Add("exchange-finished", new { id });
	void OnWebSocketMessage(WebSocketMessageInfo m) => Add("websocket-message", new { exchange = m.Number, fromClient = m.FromClient, type = m.Type.ToString(), totalBytes = m.TotalBytes });
	void OnCaptureChanged(bool enabled) => Add("capture", new { enabled });

	void Add(string name, object data) {
		TaskCompletionSource signal;
		lock (_lock) {
			_recent.Add(new ApiEvent(++_last, name, JsonSerializer.Serialize(data, Json)));
			if (_recent.Count > _capacity) _recent.RemoveRange(0, _recent.Count - _capacity);
			signal = _arrived;
			_arrived = NewSignal();
		}
		signal.TrySetResult();
	}

	/// <summary>The id of the newest event so far (0 if none).</summary>
	public long Last { get { lock (_lock) return _last; } }

	/// <summary>
	/// The events after <paramref name="id"/>, oldest first. <paramref name="missed"/> is true when some of them are no longer remembered.
	/// Also returns a task that completes when a newer event arrives.
	/// </summary>
	public (IReadOnlyList<ApiEvent> Events, bool Missed, Task Next) After(long id) {
		lock (_lock) {
			bool missed = _recent.Count > 0 ? _recent[0].Id > id + 1 : _last > id;
			return (_recent.Where(e => e.Id > id).ToList(), missed, _arrived.Task);
		}
	}

	public void Dispose() {
		_events.ExchangeStarted -= OnStarted;
		_events.ExchangeFinished -= OnFinished;
		_events.WebSocketMessage -= OnWebSocketMessage;
		_events.CaptureEnabledChanged -= OnCaptureChanged;
	}
}

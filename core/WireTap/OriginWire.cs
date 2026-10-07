namespace MoSaProxy;

/// <summary>
/// The bytes an origin sent on one proxy-to-origin connection (after TLS), exactly as they arrived, split into one response per
/// request. An HTTP/1.1 connection carries one request at a time, so the n-th response answers the n-th request written to it.
/// </summary>
sealed class OriginWire(ResourceLimitMonitor limits) : IOriginTap {
	readonly object _lock = new();
	readonly WireBuffer _fromOrigin = new();
	readonly List<ExchangeWire> _requesters = new();
	readonly List<long> _starts = new() { 0 };     // _starts[i] = absolute offset where response i begins; the last entry is the next unknown one
	bool _stopped;

	/// <summary>Called when bytes are written to the origin, with the exchange that is current at that moment (if any).</summary>
	public void Wrote(ExchangeWire? exchange, ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			if (_stopped) return;
			if (exchange is null) {
				// A write made outside any exchange (a WebSocket handshake, for one) is not something to split into responses.
				if (_requesters.Count == 0) Stop();
				return;
			}
			if (_requesters.Count > 0 && ReferenceEquals(_requesters[^1], exchange)) return;   // more of the same request
			exchange.Origin = this;
			exchange.ResponseIndex = _requesters.Count;
			_requesters.Add(exchange);
		}
	}

	public void Closed() { }

	public void Received(ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			if (_stopped) return;
			try { _fromOrigin.Append(bytes); }
			catch (OutOfMemoryException ex) {
				limits.Report("recording the bytes an origin sent", ex);
				Stop();
			}
		}
	}

	void Stop() {
		_stopped = true;
		_fromOrigin.DropBefore(_fromOrigin.EndOffset);
	}

	/// <summary>The bytes of response <paramref name="index"/>, or what has arrived of it. Null if it cannot be located.</summary>
	internal byte[]? TakeResponse(int index) {
		lock (_lock) {
			if (_stopped || index >= _requesters.Count) return null;
			// Find where each earlier response ended, in order, since each one starts where the one before it stopped.
			while (_starts.Count <= index) {
				int previous = _starts.Count - 1;
				long start = _starts[previous];
				int relative = (int)(start - _fromOrigin.BaseOffset);
				if (relative < 0 || relative > _fromOrigin.Span.Length) return null;
				var found = Http1Framing.FindResponse(_fromOrigin.Span, relative, _requesters[previous].Method);
				if (found is not { Complete: true } m) return null;
				_starts.Add(start + m.Length);
			}
			long from = _starts[index];
			if (from < _fromOrigin.BaseOffset || from > _fromOrigin.EndOffset) return null;
			var response = Http1Framing.FindResponse(_fromOrigin.Span, (int)(from - _fromOrigin.BaseOffset), _requesters[index].Method);
			int length = response is { } r ? r.Length : (int)(_fromOrigin.EndOffset - from);
			if (length <= 0) return null;
			var bytes = _fromOrigin.Copy(from, length);
			if (response is { Complete: true }) {
				if (_starts.Count == index + 1) _starts.Add(from + length);
				_fromOrigin.DropBefore(from + length);
			}
			return bytes;
		}
	}
}

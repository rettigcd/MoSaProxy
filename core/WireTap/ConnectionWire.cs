namespace MoSaProxy;

/// <summary>
/// The bytes a client sent on one connection, exactly as they arrived (after TLS, for an intercepted tunnel), so that each
/// request can be captured as the client wrote it instead of being rebuilt from what Kestrel parsed. Only HTTP/1.x is kept:
/// a connection that starts with the HTTP/2 preface, or that has been upgraded, is not recorded.
/// </summary>
public sealed class ConnectionWire {
	readonly object _lock = new();
	readonly WireBuffer _client = new();
	readonly ResourceLimitMonitor _limits;
	long _cursor;          // absolute offset where the next request starts
	long _seen;
	bool _stopped;

	internal ConnectionWire(ResourceLimitMonitor limits) => _limits = limits;

	internal void Received(ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			if (_stopped) return;
			try {
				_client.Append(bytes);
				_seen += bytes.Length;
				// HTTP/2 starts with a fixed preface; it is not HTTP/1.x text and is not kept here.
				if (_seen >= 4 && _client.BaseOffset == 0 && _client.Span.StartsWith("PRI "u8)) Stop();
			}
			catch (OutOfMemoryException ex) {
				_limits.Report("recording the bytes a client sent", ex);
				Stop();
			}
		}
	}

	/// <summary>Stops recording and forgets what was held (the connection was upgraded, for example to a WebSocket).</summary>
	public void StopRecording() { lock (_lock) Stop(); }

	void Stop() {
		_stopped = true;
		_client.DropBefore(_client.EndOffset);
	}

	/// <summary>
	/// The next request on the connection, from its first byte to its last (including the body), or what has arrived of it if the
	/// client stopped sending part way. Requests are taken in order, one per exchange. Null if nothing is held.
	/// </summary>
	public byte[]? TakeNextRequest() {
		lock (_lock) {
			if (_stopped || _client.EndOffset <= _cursor) return null;
			int relative = (int)(_cursor - _client.BaseOffset);
			var found = Http1Framing.FindRequest(_client.Span, relative);
			int length = found is { } m ? m.Length : (int)(_client.EndOffset - _cursor);
			var bytes = _client.Copy(_cursor, length);
			_cursor += length;
			_client.DropBefore(_cursor);
			return bytes;
		}
	}
}

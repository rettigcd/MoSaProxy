namespace MoSaProxy;

/// <summary>
/// The bytes a client sent on one connection, exactly as they arrived (after TLS, for an intercepted tunnel), so that each request
/// can be captured as the client wrote it instead of being rebuilt from what Kestrel parsed. An HTTP/1.x connection is kept as bytes
/// and split into requests; a connection that starts with the HTTP/2 preface is followed frame by frame, and each stream's request
/// is written out as HTTP/1.1-style text from its decoded headers. An upgraded connection (a WebSocket) is not recorded.
/// </summary>
public sealed class ConnectionWire {
	readonly object _lock = new();
	readonly WireBuffer _client = new();
	readonly ResourceLimitMonitor _limits;
	Http2Direction? _http2;
	long _cursor;          // absolute offset where the next HTTP/1.x request starts
	long _seen;
	bool _stopped;

	internal ConnectionWire(ResourceLimitMonitor limits) => _limits = limits;

	/// <summary>True when the client speaks HTTP/2 on this connection (it started with the HTTP/2 preface).</summary>
	public bool IsHttp2 { get { lock (_lock) return _http2 is not null; } }

	internal void Received(ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			if (_stopped) return;
			try {
				if (_http2 is not null) { _http2.Received(bytes); return; }
				_client.Append(bytes);
				_seen += bytes.Length;
				if (_seen >= 4 && _client.BaseOffset == 0 && _client.Span.StartsWith("PRI "u8)) {
					// HTTP/2 starts with a fixed preface. From here the bytes are frames, not HTTP/1.x text.
					_http2 = new Http2Direction(hasPreface: true, isRequestDirection: true);
					_http2.Received(_client.Span);
					_client.DropBefore(_client.EndOffset);
				}
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

	/// <summary>Stops keeping anything for one HTTP/2 stream, such as a WebSocket that runs over it.</summary>
	public void IgnoreHttp2Stream(int streamId) { lock (_lock) _http2?.Ignore(streamId); }

	/// <summary>
	/// The next HTTP/1.x request on the connection, from its first byte to its last (including the body), or what has arrived of it if the
	/// client stopped sending part way. Requests are taken in order, one per exchange. Null if nothing is held.
	/// </summary>
	public byte[]? TakeNextRequest() {
		lock (_lock) {
			if (_stopped || _http2 is not null || _client.EndOffset <= _cursor) return null;
			int relative = (int)(_cursor - _client.BaseOffset);
			var found = Http1Framing.FindRequest(_client.Span, relative);
			int length = found is { } m ? m.Length : (int)(_client.EndOffset - _cursor);
			var bytes = _client.Copy(_cursor, length);
			_cursor += length;
			_client.DropBefore(_cursor);
			return bytes;
		}
	}

	/// <summary>The request on an HTTP/2 stream, written out as HTTP/1.1-style text (or what has arrived of it). Null if nothing is held for the stream.</summary>
	public byte[]? TakeHttp2Request(int streamId) {
		lock (_lock) {
			if (_stopped || _http2 is null || _http2.Take(streamId) is not { } stream) return null;
			return Http2Rendering.Request(stream);
		}
	}
}

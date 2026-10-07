namespace MoSaProxy;

/// <summary>
/// The frames of one proxy-to-origin HTTP/2 connection (after TLS), both ways. The stream ids on this connection are not the
/// client's, and requests are written by the connection's own writer loop, so an exchange is found by what it asked for: the
/// method, <c>:authority</c> and <c>:path</c> of the requests this connection carried, read from the request headers.
/// </summary>
sealed class Http2OriginWire : IOriginTap {
	readonly object _lock = new();
	readonly Http2Direction _requests;
	readonly Http2Direction _responses;
	readonly ResourceLimitMonitor _limits;

	public Http2OriginWire(ResourceLimitMonitor limits) {
		_limits = limits;
		_requests = new Http2Direction(hasPreface: true, isRequestDirection: true);
		_responses = new Http2Direction(hasPreface: false, isRequestDirection: false, isIgnoredElsewhere: _requests.IsIgnored);
	}

	/// <summary>When the connection was closed, so that an old connection with unclaimed streams can be forgotten.</summary>
	public DateTime? ClosedAt { get; private set; }

	/// <summary>True when nothing is held for any stream.</summary>
	public bool IsEmpty { get { lock (_lock) return !_requests.StreamIds.Any() && !_responses.StreamIds.Any(); } }

	public void Wrote(ExchangeWire? exchange, ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			try { _requests.Received(bytes); }
			catch (OutOfMemoryException ex) { _limits.Report("recording the frames sent to an origin", ex); }
		}
	}

	public void Received(ReadOnlySpan<byte> bytes) {
		lock (_lock) {
			try { _responses.Received(bytes); }
			catch (OutOfMemoryException ex) { _limits.Report("recording the frames an origin sent", ex); }
		}
	}

	public void Closed() { lock (_lock) ClosedAt = DateTime.UtcNow; }

	/// <summary>
	/// Finds the stream that carried this request, and takes its response as HTTP/1.1-style text. Prefers a response that has ended;
	/// of several identical requests the one with the lowest stream id goes first. Null if no such stream has a response.
	/// </summary>
	public byte[]? TakeResponse(string method, string authority, string pathAndQuery) {
		lock (_lock) {
			int? chosen = null;
			foreach (int id in _requests.StreamIds) {
				var request = _requests.Peek(id)?.HeaderBlocks.FirstOrDefault();
				if (request is null || !Matches(request, method, authority, pathAndQuery)) continue;
				var response = _responses.Peek(id);
				if (response is null) continue;
				if (response.Ended) { chosen = id; break; }
				chosen ??= id;
			}
			if (chosen is not { } streamId) return null;
			_requests.Take(streamId);
			var taken = _responses.Take(streamId);
			return taken is null ? null : Http2Rendering.Response(taken);
		}
	}

	static bool Matches(List<KeyValuePair<string, string>> request, string method, string authority, string pathAndQuery) {
		string Get(string name) => request.FirstOrDefault(h => h.Key == name).Value ?? "";
		return Get(":method") == method && Get(":authority") == authority && Get(":path") == pathAndQuery;
	}
}

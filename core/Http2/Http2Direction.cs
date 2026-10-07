namespace MoSaProxy;

/// <summary>
/// Follows the frames of one direction of an HTTP/2 connection (the client's bytes, or the server's): it splits the bytes into
/// frames, decodes every header block (the dynamic table needs all of them, in order), and keeps each stream's headers and body
/// until they are claimed. Not thread-safe: the owner locks. Any malformed input makes it give up for the rest of the connection.
/// </summary>
sealed class Http2Direction(bool hasPreface, bool isRequestDirection, Func<int, bool>? isIgnoredElsewhere = null) {
	const int FrameHeaderLength = 9;
	static readonly byte[] Preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

	readonly WireBuffer _buffer = new();
	readonly HpackDecoder _hpack = new();
	readonly Dictionary<int, Http2Stream> _streams = new();
	readonly HashSet<int> _ignored = new();
	bool _prefaceSeen = !hasPreface;

	// A header block can be split over a HEADERS frame and CONTINUATION frames; it is decoded when the last one arrives.
	int _blockStream;
	bool _blockEndsStream;
	MemoryStream? _block;

	/// <summary>True once the bytes could not be understood; nothing more is kept.</summary>
	public bool Failed { get; private set; }

	public void Received(ReadOnlySpan<byte> bytes) {
		if (Failed) return;
		_buffer.Append(bytes);
		try { Parse(); }
		catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) { Fail(); }
	}

	void Fail() {
		Failed = true;
		_buffer.DropBefore(_buffer.EndOffset);
		_streams.Clear();
	}

	void Parse() {
		var data = _buffer.Span;
		int pos = 0;
		if (!_prefaceSeen) {
			if (data.Length < Preface.Length) return;
			if (!data[..Preface.Length].SequenceEqual(Preface)) throw new InvalidDataException("Not an HTTP/2 connection preface.");
			pos = Preface.Length;
			_prefaceSeen = true;
		}
		while (data.Length - pos >= FrameHeaderLength) {
			int length = data[pos] << 16 | data[pos + 1] << 8 | data[pos + 2];
			if (data.Length - pos < FrameHeaderLength + length) break;
			byte type = data[pos + 3], flags = data[pos + 4];
			int streamId = (data[pos + 5] & 0x7F) << 24 | data[pos + 6] << 16 | data[pos + 7] << 8 | data[pos + 8];
			Frame(type, flags, streamId, data.Slice(pos + FrameHeaderLength, length));
			pos += FrameHeaderLength + length;
		}
		_buffer.DropBefore(_buffer.BaseOffset + pos);
	}

	void Frame(byte type, byte flags, int streamId, ReadOnlySpan<byte> payload) {
		if (_block is not null && type != 9) throw new InvalidDataException("A header block was interrupted by another frame.");
		switch (type) {
			case 0:     // DATA
				if (streamId == 0) throw new InvalidDataException("DATA on stream 0.");
				KeepBody(streamId, Unpad(flags, payload));
				if ((flags & 0x1) != 0) End(streamId);
				break;
			case 1: {   // HEADERS
				if (streamId == 0) throw new InvalidDataException("HEADERS on stream 0.");
				var fragment = Unpad(flags, payload);
				if ((flags & 0x20) != 0) fragment = fragment[5..];          // priority: stream dependency and weight
				_blockStream = streamId;
				_blockEndsStream = (flags & 0x1) != 0;
				_block = new MemoryStream();
				_block.Write(fragment);
				if ((flags & 0x4) != 0) FinishBlock();
				break;
			}
			case 9:     // CONTINUATION
				if (_block is null || streamId != _blockStream) throw new InvalidDataException("Unexpected CONTINUATION.");
				_block.Write(payload);
				if ((flags & 0x4) != 0) FinishBlock();
				break;
			case 3:     // RST_STREAM
				End(streamId);
				break;
			case 5:     // PUSH_PROMISE carries a header block that would change the dynamic table; it is not supported (push is not enabled)
				throw new InvalidDataException("PUSH_PROMISE is not supported.");
		}
	}

	static ReadOnlySpan<byte> Unpad(byte flags, ReadOnlySpan<byte> payload) {
		if ((flags & 0x8) == 0) return payload;
		int padding = payload[0];
		if (1 + padding > payload.Length) throw new InvalidDataException("Padding is longer than the frame.");
		return payload[1..^padding];
	}

	void FinishBlock() {
		var headers = _hpack.Decode(_block!.GetBuffer().AsSpan(0, (int)_block.Length));   // always decoded, even for a stream nobody wants
		_block = null;
		int id = _blockStream;
		if (IsIgnored(id)) return;
		// A request that is not an ordinary request-and-response (a CONNECT, which is how WebSockets work over HTTP/2) can run forever.
		if (isRequestDirection && headers.Any(h => h.Key == ":protocol" || (h.Key == ":method" && h.Value == "CONNECT"))) {
			Ignore(id);
			return;
		}
		GetOrAdd(id).HeaderBlocks.Add(headers);
		if (_blockEndsStream) End(id);
	}

	void KeepBody(int streamId, ReadOnlySpan<byte> data) {
		if (IsIgnored(streamId) || !_streams.TryGetValue(streamId, out var stream)) return;
		stream.Body.Write(data);
	}

	void End(int streamId) {
		if (_streams.TryGetValue(streamId, out var stream)) stream.Ended = true;
	}

	Http2Stream GetOrAdd(int streamId) {
		if (!_streams.TryGetValue(streamId, out var stream)) _streams[streamId] = stream = new Http2Stream();
		return stream;
	}

	/// <summary>Stops keeping anything for this stream (and forgets what was kept).</summary>
	public void Ignore(int streamId) {
		_ignored.Add(streamId);
		_streams.Remove(streamId);
	}

	public bool IsIgnored(int streamId) => _ignored.Contains(streamId) || (isIgnoredElsewhere?.Invoke(streamId) ?? false);

	/// <summary>The stream, if anything has been kept for it. It stays until <see cref="Take"/>.</summary>
	public Http2Stream? Peek(int streamId) => _streams.GetValueOrDefault(streamId);

	/// <summary>The ids of the streams that are being kept, in ascending order.</summary>
	public IEnumerable<int> StreamIds => _streams.Keys.Order().ToList();

	/// <summary>Removes the stream and returns it, or null if nothing was kept.</summary>
	public Http2Stream? Take(int streamId) => _streams.Remove(streamId, out var stream) ? stream : null;
}

namespace MoSaProxy;

/// <summary>
/// Bytes seen on one side of a connection. Offsets are absolute (counted from the first byte ever seen), so the front can be
/// dropped once the messages in it have been claimed without the offsets of the later ones changing. Not thread-safe: the owner locks.
/// </summary>
sealed class WireBuffer {
	byte[] _data = new byte[4096];
	int _count;

	/// <summary>The absolute offset of the first byte still held.</summary>
	public long BaseOffset { get; private set; }

	/// <summary>The absolute offset just past the last byte held.</summary>
	public long EndOffset => BaseOffset + _count;

	public void Append(ReadOnlySpan<byte> bytes) {
		if (_count + bytes.Length > _data.Length) Array.Resize(ref _data, (int)Math.Max((long)_data.Length * 2, (long)_count + bytes.Length));
		bytes.CopyTo(_data.AsSpan(_count));
		_count += bytes.Length;
	}

	/// <summary>The bytes held, starting at <see cref="BaseOffset"/>.</summary>
	public ReadOnlySpan<byte> Span => _data.AsSpan(0, _count);

	/// <summary>Forgets everything before the absolute offset <paramref name="offset"/>.</summary>
	public void DropBefore(long offset) {
		int drop = (int)Math.Clamp(offset - BaseOffset, 0, _count);
		if (drop == 0) return;
		Buffer.BlockCopy(_data, drop, _data, 0, _count - drop);
		_count -= drop;
		BaseOffset += drop;
		if (_count == 0 && _data.Length > 1024 * 1024) _data = new byte[4096];   // do not keep a large buffer for a quiet connection
	}

	/// <summary>Copies <paramref name="length"/> bytes starting at the absolute offset <paramref name="start"/>.</summary>
	public byte[] Copy(long start, int length) => _data.AsSpan((int)(start - BaseOffset), length).ToArray();
}

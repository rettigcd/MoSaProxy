namespace MoSaProxy;

/// <summary>
/// Passes reads through to <c>inner</c> and copies the bytes read into <c>sink</c>. A failing sink is dropped
/// silently (the copy stops, the read keeps working). Copying stops after <c>maxBytes</c> and sets <see cref="Truncated"/>.
/// </summary>
public sealed class TeeReadStream(Stream inner, Stream? sink, long? maxBytes, Action<Exception>? onSinkFailure = null) : Stream {
	long _copied;
	bool _sinkFailed;

	public bool Truncated { get; private set; }

	/// <summary>How many bytes have been read through this stream, whether or not they were copied.</summary>
	public long BytesRead { get; private set; }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		int n = await inner.ReadAsync(buffer, cancellationToken);
		BytesRead += n;
		if (n > 0 && sink is not null && !_sinkFailed) {
			var chunk = buffer[..n];
			if (maxBytes is long max && _copied + n > max) {
				chunk = chunk[..(int)Math.Max(0, max - _copied)];
				Truncated = true;
			}
			try {
				if (chunk.Length > 0) await sink.WriteAsync(chunk, cancellationToken);
				_copied += chunk.Length;
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) {
				_sinkFailed = true;
				onSinkFailure?.Invoke(ex);
			}
		}
		return n;
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync.");
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

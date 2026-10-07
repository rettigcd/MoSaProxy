namespace MoSaProxy;

/// <summary>Wraps a stream and counts the bytes read from and written to it.</summary>
public sealed class CountingStream(Stream inner) : Stream {
	public long BytesRead { get; private set; }
	public long BytesWritten { get; private set; }

	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => false;
	public override bool CanWrite => inner.CanWrite;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		int n = await inner.ReadAsync(buffer, cancellationToken);
		BytesRead += n;
		return n;
	}

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		await inner.WriteAsync(buffer, cancellationToken);
		BytesWritten += buffer.Length;
	}

	public override int Read(byte[] buffer, int offset, int count) {
		int n = inner.Read(buffer, offset, count);
		BytesRead += n;
		return n;
	}

	public override void Write(byte[] buffer, int offset, int count) {
		inner.Write(buffer, offset, count);
		BytesWritten += count;
	}

	public override void Flush() => inner.Flush();
	public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}

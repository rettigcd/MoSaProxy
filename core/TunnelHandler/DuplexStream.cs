namespace MoSaProxy;

/// <summary>Combines a read-only stream and a write-only stream into one duplex stream.</summary>
sealed class DuplexStream(Stream readSide, Stream writeSide) : Stream {
	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => readSide.ReadAsync(buffer, cancellationToken);
	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		await writeSide.WriteAsync(buffer, cancellationToken);
		await writeSide.FlushAsync(cancellationToken);
	}
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}

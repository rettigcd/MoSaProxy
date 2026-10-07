namespace MoSaProxy;

/// <summary>
/// Wraps the plain-text stream of one proxy-to-origin connection (after TLS) and records what the origin sends, noting which
/// exchange each request write belongs to. Everything passes through unchanged.
/// </summary>
sealed class OriginTapStream(Stream inner, OriginWire wire) : Stream {
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		int n = await inner.ReadAsync(buffer, cancellationToken);
		wire.Received(buffer.Span[..n]);
		return n;
	}

	public override int Read(byte[] buffer, int offset, int count) {
		int n = inner.Read(buffer, offset, count);
		wire.Received(buffer.AsSpan(offset, n));
		return n;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
		wire.Wrote(ExchangeWire.Current.Value);
		return inner.WriteAsync(buffer, cancellationToken);
	}

	public override void Write(byte[] buffer, int offset, int count) {
		wire.Wrote(ExchangeWire.Current.Value);
		inner.Write(buffer, offset, count);
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override void Flush() => inner.Flush();
	public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();

	protected override void Dispose(bool disposing) {
		if (disposing) inner.Dispose();
		base.Dispose(disposing);
	}

	public override ValueTask DisposeAsync() => inner.DisposeAsync();
}

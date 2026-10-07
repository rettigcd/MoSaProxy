using System.IO.Pipelines;

namespace MoSaProxy;

/// <summary>Passes everything through to <c>inner</c> and hands each byte committed to it to <c>wire</c>, so that a response the HTTP layer wrote itself can be seen.</summary>
sealed class TapWriter(PipeWriter inner, ConnectionWire wire) : PipeWriter {
	Memory<byte> _current;

	public override Memory<byte> GetMemory(int sizeHint = 0) => _current = inner.GetMemory(sizeHint);
	public override Span<byte> GetSpan(int sizeHint = 0) => (_current = inner.GetMemory(sizeHint)).Span;

	public override void Advance(int bytes) {
		wire.Sent(_current.Span[..bytes]);
		inner.Advance(bytes);
	}

	public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
	public override void CancelPendingFlush() => inner.CancelPendingFlush();
	public override void Complete(Exception? exception = null) => inner.Complete(exception);
}

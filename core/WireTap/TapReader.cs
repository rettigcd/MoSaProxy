using System.Buffers;
using System.IO.Pipelines;

namespace MoSaProxy;

/// <summary>
/// Passes everything through to <c>inner</c> and hands each byte it delivers to <c>wire</c> exactly once, at the moment it is
/// delivered rather than when the consumer consumes it, so that bytes Kestrel reads but then rejects (a malformed request) are not lost.
/// </summary>
sealed class TapReader(PipeReader inner, ConnectionWire wire) : PipeReader {
	long _startAbs, _reportedAbs;   // absolute offset of the first unconsumed byte; absolute offset already reported
	ReadOnlySequence<byte> _last;

	void Remember(ReadResult result) {
		_last = result.Buffer;
		long end = _startAbs + _last.Length;
		if (end <= _reportedAbs) return;
		foreach (var segment in _last.Slice(_reportedAbs - _startAbs)) wire.Received(segment.Span);
		_reportedAbs = end;
	}

	void Consumed(SequencePosition consumed) {
		_startAbs += _last.Slice(_last.Start, consumed).Length;
		_last = _last.Slice(consumed);
	}

	public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) {
		var result = await inner.ReadAsync(cancellationToken);
		Remember(result);
		return result;
	}

	public override bool TryRead(out ReadResult result) {
		bool ok = inner.TryRead(out result);
		if (ok) Remember(result);
		return ok;
	}

	public override void AdvanceTo(SequencePosition consumed) { Consumed(consumed); inner.AdvanceTo(consumed); }
	public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) { Consumed(consumed); inner.AdvanceTo(consumed, examined); }
	public override void CancelPendingRead() => inner.CancelPendingRead();
	public override void Complete(Exception? exception = null) => inner.Complete(exception);
}

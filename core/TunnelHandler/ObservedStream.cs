using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace MoSaProxy;

/// <summary>Passes everything through to <c>inner</c>, remembering how much was read and the first read failure (if any).</summary>
sealed class ObservedStream(Stream inner) : Stream {
	public long BytesRead { get; private set; }
	public Exception? ReadFailure { get; private set; }

	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => false;
	public override bool CanWrite => inner.CanWrite;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		try {
			int n = await inner.ReadAsync(buffer, cancellationToken);
			BytesRead += n;
			return n;
		}
		catch (Exception ex) when (ex is not OperationCanceledException) {
			ReadFailure ??= ex;
			throw;
		}
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
	public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync.");
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync.");
	public override void Flush() => inner.Flush();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
	public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); await base.DisposeAsync(); }
}

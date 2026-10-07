using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MoSaProxy.Tests;

/// <summary>A raw TCP client for talking to the proxy byte by byte, as a browser would for a CONNECT tunnel.</summary>
public sealed class RawClient : IDisposable {
	readonly TcpClient _tcp = new();
	NetworkStream _stream = null!;
	byte[] _pending = Array.Empty<byte>();   // bytes already received but not yet asked for (a read can run past what the caller wanted)

	public static async Task<RawClient> ConnectAsync(int port) {
		var client = new RawClient();
		await client._tcp.ConnectAsync("127.0.0.1", port);
		client._stream = client._tcp.GetStream();
		return client;
	}

	public async Task SendAsync(string text) => await _stream.WriteAsync(Encoding.Latin1.GetBytes(text));
	public async Task SendAsync(byte[] bytes) => await _stream.WriteAsync(bytes);

	/// <summary>Tells the other side we will send no more data, while still being able to read.</summary>
	public void FinishSending() => _tcp.Client.Shutdown(SocketShutdown.Send);

	/// <summary>Reads from what was received earlier first, then from the connection.</summary>
	async Task<int> ReadSomeAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
		if (_pending.Length == 0) return await _stream.ReadAsync(buffer, cancellationToken);
		int n = Math.Min(buffer.Length, _pending.Length);
		_pending.AsMemory(0, n).CopyTo(buffer);
		_pending = _pending[n..];
		return n;
	}

	/// <summary>
	/// Reads until the given text has been received and returns everything up to and including it. Anything that arrived
	/// after it (the next bytes can share a network read with it) is kept for the next read instead of being lost.
	/// If the connection ends first, returns what was received.
	/// </summary>
	public async Task<string> ReadUntilAsync(string marker, int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		var received = new List<byte>();
		var buffer = new byte[4096];
		while (true) {
			string text = Encoding.Latin1.GetString(received.ToArray());
			int at = text.IndexOf(marker, StringComparison.Ordinal);
			if (at >= 0) {
				int end = at + marker.Length;
				_pending = received.Skip(end).Concat(_pending).ToArray();
				return text[..end];
			}
			int n = await ReadSomeAsync(buffer, cts.Token);
			if (n == 0) return text;
			received.AddRange(buffer.AsSpan(0, n).ToArray());
		}
	}

	/// <summary>Reads exactly <paramref name="count"/> bytes (fewer only if the connection closes first).</summary>
	public async Task<byte[]> ReadExactlyAsync(int count, int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		var buffer = new byte[count];
		int total = 0;
		while (total < count) {
			int n = await ReadSomeAsync(buffer.AsMemory(total), cts.Token);
			if (n == 0) break;
			total += n;
		}
		return buffer[..total];
	}

	/// <summary>Reads until the other side closes the connection.</summary>
	public async Task<byte[]> ReadToEndAsync(int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		using var all = new MemoryStream();
		all.Write(_pending);
		_pending = Array.Empty<byte>();
		await _stream.CopyToAsync(all, cts.Token);
		return all.ToArray();
	}

	public void Dispose() => _tcp.Dispose();
}

/// <summary>
/// A fake origin that speaks plain TCP (no HTTP) on loopback, for testing tunnels.
/// The handler gets each accepted connection's stream.
/// </summary>
public sealed class TestTcpOrigin : IAsyncDisposable {
	readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	readonly CancellationTokenSource _stop = new();
	readonly Task _acceptLoop;
	readonly List<byte[]> _received = new();

	public TestTcpOrigin(Func<Stream, Task> handler) {
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptLoop = Task.Run(async () => {
			while (!_stop.IsCancellationRequested) {
				TcpClient connection;
				try { connection = await _listener.AcceptTcpClientAsync(_stop.Token); }
				catch (OperationCanceledException) { return; }
				_ = Task.Run(async () => {
					using (connection) {
						try { await handler(connection.GetStream()); } catch { }
					}
				});
			}
		});
	}

	public int Port { get; }

	/// <summary>Echoes every byte back until the client finishes sending.</summary>
	public static TestTcpOrigin Echo() => new(async stream => await stream.CopyToAsync(stream));

	public async ValueTask DisposeAsync() {
		_stop.Cancel();
		_listener.Stop();
		try { await _acceptLoop; } catch { }
	}
}

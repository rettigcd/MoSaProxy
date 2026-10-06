using System.Net;
using System.Net.Sockets;
using System.Text;

namespace mos.Tests;

/// <summary>A raw TCP client for talking to the proxy byte by byte, as a browser would for a CONNECT tunnel.</summary>
public sealed class RawClient : IDisposable {
	readonly TcpClient _tcp = new();
	NetworkStream _stream = null!;

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

	/// <summary>Reads until the given text has been received (and returns everything read so far).</summary>
	public async Task<string> ReadUntilAsync(string marker, int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		var received = new StringBuilder();
		var buffer = new byte[4096];
		while (!received.ToString().Contains(marker)) {
			int n = await _stream.ReadAsync(buffer, cts.Token);
			if (n == 0) break;
			received.Append(Encoding.Latin1.GetString(buffer, 0, n));
		}
		return received.ToString();
	}

	/// <summary>Reads exactly <paramref name="count"/> bytes (fewer only if the connection closes first).</summary>
	public async Task<byte[]> ReadExactlyAsync(int count, int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		var buffer = new byte[count];
		int total = 0;
		while (total < count) {
			int n = await _stream.ReadAsync(buffer.AsMemory(total), cts.Token);
			if (n == 0) break;
			total += n;
		}
		return buffer[..total];
	}

	/// <summary>Reads until the other side closes the connection.</summary>
	public async Task<byte[]> ReadToEndAsync(int timeoutSeconds = 10) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		using var all = new MemoryStream();
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

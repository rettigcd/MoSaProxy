using System.Net.Sockets;

namespace MoSaProxy;

/// <summary>Opens the connection for a CONNECT request and relays bytes in both directions.</summary>
public static class TunnelRelay {

	/// <summary>While the proxy is shutting down, a tunnel with no data moving for this long is closed.</summary>
	public static readonly TimeSpan StoppingQuietPeriod = TimeSpan.FromSeconds(1);

	static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

	/// <summary>Parses the "host:port" target of a CONNECT request.</summary>
	public static bool TryParseTarget(string target, out string host, out int port) {
		host = "";
		port = 0;
		int colon = target.LastIndexOf(':');
		if (colon <= 0 || !int.TryParse(target[(colon + 1)..], out port) || port < 1 || port > 65535) return false;
		host = target[..colon].Trim('[', ']');
		return host.Length > 0;
	}

	/// <summary>Connects to the origin. Throws SocketException on failure and OperationCanceledException on timeout.</summary>
	public static async Task<TcpClient> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken) {
		var origin = new TcpClient { NoDelay = true };
		try {
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(timeout);
			await origin.ConnectAsync(host, port, cts.Token);
			return origin;
		}
		catch {
			origin.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Copies bytes between the client and the origin until the origin closes, the client disconnects,
	/// or no data moves in either direction for <paramref name="idleTimeout"/>.
	/// A client that finishes sending first only half-closes the origin, so the response can still arrive.
	/// Once <paramref name="stopping"/> is signalled (the proxy is shutting down) the tunnel is given a short quiet period
	/// instead of the full idle timeout: a transfer that is still moving data carries on, an idle one ends.
	/// </summary>
	public static async Task<(long ToOrigin, long FromOrigin)> RelayAsync(
		Stream client, TcpClient originConnection, TimeSpan idleTimeout, CancellationToken cancellationToken, CancellationToken stopping = default) {

		using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var origin = originConnection.GetStream();
		long toOrigin = 0, fromOrigin = 0;
		long lastActivity = Environment.TickCount64;

		async Task PumpAsync(Stream from, Stream to, bool towardsOrigin) {
			var buffer = new byte[16 * 1024];
			while (true) {
				int n = await from.ReadAsync(buffer, done.Token);
				if (n == 0) return;
				await to.WriteAsync(buffer.AsMemory(0, n), done.Token);
				Volatile.Write(ref lastActivity, Environment.TickCount64);
				if (towardsOrigin) Interlocked.Add(ref toOrigin, n);
				else Interlocked.Add(ref fromOrigin, n);
			}
		}

		async Task WatchIdleAsync() {
			while (!done.IsCancellationRequested) {
				var limit = stopping.IsCancellationRequested && StoppingQuietPeriod < idleTimeout ? StoppingQuietPeriod : idleTimeout;
				var remaining = limit - TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref lastActivity));
				if (remaining <= TimeSpan.Zero) { done.Cancel(); return; }
				// Wake up when the limit could be reached, but also soon enough to notice that shutdown has started.
				var wait = stopping.IsCancellationRequested || remaining < PollInterval ? remaining : PollInterval;
				try { await Task.Delay(wait, done.Token); } catch (OperationCanceledException) { return; }
			}
		}

		// Client -> origin. When the client is finished sending, only half-close the origin so its reply can still arrive.
		var upstream = Task.Run(async () => {
			try {
				await PumpAsync(client, origin, towardsOrigin: true);
				try { originConnection.Client.Shutdown(SocketShutdown.Send); } catch { }
			}
			catch { done.Cancel(); }
		});

		// Origin -> client. When the origin is finished (or anything fails) the whole tunnel is over.
		var downstream = Task.Run(async () => {
			try { await PumpAsync(origin, client, towardsOrigin: false); }
			catch { }
			finally { done.Cancel(); }
		});

		await Task.WhenAll(upstream, downstream, WatchIdleAsync());
		return (Interlocked.Read(ref toOrigin), Interlocked.Read(ref fromOrigin));
	}
}

namespace mos;

public enum ActivityKind {
	/// <summary>An HTTP request being forwarded (including one inside a decrypted tunnel).</summary>
	Exchange,
	/// <summary>A tunnel being relayed byte for byte (a blind CONNECT, or a protocol that is neither TLS nor HTTP).</summary>
	Tunnel,
	/// <summary>An open WebSocket connection.</summary>
	WebSocket,
}

/// <summary>What the proxy is doing right now: how many requests, tunnels and WebSockets are in flight. Used to report on and wait for a shutdown.</summary>
public sealed class ProxyActivity {
	int _exchanges, _tunnels, _webSockets;

	public int Exchanges => Volatile.Read(ref _exchanges);
	public int Tunnels => Volatile.Read(ref _tunnels);
	public int WebSockets => Volatile.Read(ref _webSockets);
	public int Total => Exchanges + Tunnels + WebSockets;

	/// <summary>Counts one more item of this kind until the returned scope is disposed.</summary>
	public IDisposable Track(ActivityKind kind) {
		ref int counter = ref Counter(kind);
		Interlocked.Increment(ref counter);
		return new Scope(this, kind);
	}

	/// <summary>Waits until nothing is in flight, up to <paramref name="timeout"/>. Returns whether everything finished.</summary>
	public async Task<bool> WaitForIdleAsync(TimeSpan timeout) {
		var deadline = DateTime.UtcNow + timeout;
		while (Total > 0) {
			if (DateTime.UtcNow >= deadline) return false;
			await Task.Delay(20);
		}
		return true;
	}

	ref int Counter(ActivityKind kind) {
		switch (kind) {
			case ActivityKind.Exchange: return ref _exchanges;
			case ActivityKind.Tunnel: return ref _tunnels;
			default: return ref _webSockets;
		}
	}

	sealed class Scope(ProxyActivity owner, ActivityKind kind) : IDisposable {
		int _disposed;
		public void Dispose() {
			if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner.Counter(kind));
		}
	}
}

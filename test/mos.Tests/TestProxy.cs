using System.Net;

namespace mos.Tests;

public sealed class MemoryExchangeLog : IExchangeLog {
	readonly List<ExchangeRecord> _records = new();
	public IReadOnlyList<ExchangeRecord> Records { get { lock (_records) return _records.ToArray(); } }
	public void Write(ExchangeRecord record) { lock (_records) _records.Add(record); }
}

/// <summary>Runs the real proxy on a free loopback port and provides an HttpClient that talks through it.</summary>
public sealed class TestProxy : IAsyncDisposable {
	readonly ProxyHost _host;

	TestProxy(ProxyHost host, MemoryExchangeLog log) {
		_host = host;
		Log = log;
		Client = new HttpClient(new HttpClientHandler {
			Proxy = new WebProxy($"http://127.0.0.1:{host.Port}"),
			UseProxy = true,
			AllowAutoRedirect = false,
			UseCookies = false,
		}) { Timeout = TimeSpan.FromSeconds(30) };
	}

	public MemoryExchangeLog Log { get; }
	public HttpClient Client { get; }
	public int Port => _host.Port;

	public static async Task<TestProxy> StartAsync(ProxyOptions? options = null) {
		var log = new MemoryExchangeLog();
		var host = await ProxyHost.StartAsync(options ?? new ProxyOptions { Port = 0 }, log);
		return new TestProxy(host, log);
	}

	public async ValueTask DisposeAsync() {
		Client.Dispose();
		await _host.DisposeAsync();
	}
}

using System.Net;

namespace mos.Tests;

public sealed class MemoryExchangeLog : IExchangeLog {
	readonly List<ExchangeRecord> _records = new();
	public IReadOnlyList<ExchangeRecord> Records { get { lock (_records) return _records.ToArray(); } }
	public void Write(ExchangeRecord record) { lock (_records) _records.Add(record); }

	/// <summary>
	/// The proxy logs an exchange just after the client receives the response, so a test that reads the log
	/// immediately could race it. This waits until the expected number of records has arrived.
	/// </summary>
	public async Task<IReadOnlyList<ExchangeRecord>> WaitForAsync(int count = 1) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (Records.Count < count && DateTime.UtcNow < deadline) await Task.Delay(10);
		return Records;
	}
}

/// <summary>A temporary folder that is deleted when disposed.</summary>
public sealed class TempFolder : IDisposable {
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mos-test-{Guid.NewGuid():N}");

	public TempFolder() => Directory.CreateDirectory(Path);

	/// <summary>Waits for a file to appear (capture metadata is written just after the client gets its response).</summary>
	public static async Task<string> WaitForFileAsync(string path) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!File.Exists(path) && DateTime.UtcNow < deadline) await Task.Delay(10);
		return path;
	}

	public void Dispose() {
		try { Directory.Delete(Path, recursive: true); } catch { }
	}
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
	public string? CaptureRunDirectory => _host.CaptureRunDirectory;

	public static async Task<TestProxy> StartAsync(ProxyOptions? options = null, ISessionCapture? capture = null) {
		var log = new MemoryExchangeLog();
		var host = await ProxyHost.StartAsync(options ?? new ProxyOptions { Port = 0 }, log, capture);
		return new TestProxy(host, log);
	}

	public async ValueTask DisposeAsync() {
		Client.Dispose();
		await _host.DisposeAsync();
	}
}

using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MoSaProxy.Tests;

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

/// <summary>Runs the real proxy on a free loopback port and provides an HttpClient that talks through it.</summary>
public sealed class TestProxy : IAsyncDisposable {
	readonly ProxyHost _host;

	readonly bool _clientTrustsProxyCa;

	TestProxy(ProxyHost host, MemoryExchangeLog log, bool clientTrustsProxyCa) {
		_host = host;
		_clientTrustsProxyCa = clientTrustsProxyCa;
		Log = log;
		HttpClient Build(Version requestVersion) {
			var handler = new HttpClientHandler {
				Proxy = new WebProxy($"http://127.0.0.1:{host.Port}"),
				UseProxy = true,
				AllowAutoRedirect = false,
				UseCookies = false,
			};
			// Like a browser with the MoSaProxy root installed: accept a certificate only if it names the right host and chains to the MoSaProxy root.
			// Without that trust, certificate errors are left alone so the client behaves like a browser that has not installed it.
			handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => ValidateServerCertificate(certificate, errors);
			return new HttpClient(handler) {
				Timeout = TimeSpan.FromSeconds(30),
				DefaultRequestVersion = requestVersion,
				DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
			};
		}
		Client = Build(HttpVersion.Version11);
		Http2Client = Build(HttpVersion.Version20);
	}

	/// <summary>
	/// Like a browser with the MoSaProxy root installed: accept a certificate only if it names the right host and chains to the MoSaProxy root.
	/// Without that trust, certificate errors are left alone so the client behaves like a browser that has not installed it.
	/// </summary>
	bool ValidateServerCertificate(X509Certificate2? certificate, SslPolicyErrors errors) {
		if (certificate is not null) ServerCertificateSeenByClient = new X509Certificate2(certificate);
		if (certificate is not null && AlsoTrust.Contains(certificate.Thumbprint)) return true;
		if (!_clientTrustsProxyCa || _host.Authority is null) return errors == SslPolicyErrors.None;
		if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 || certificate is null) return false;
		using var chain = new X509Chain();
		chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
		chain.ChainPolicy.CustomTrustStore.Add(_host.Authority.Root);
		chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
		return chain.Build(certificate);
	}

	/// <summary>A WebSocket client that goes through the proxy and validates certificates like <see cref="Client"/> does. Not connected yet.</summary>
	public System.Net.WebSockets.ClientWebSocket CreateWebSocket(Action<System.Net.WebSockets.ClientWebSocketOptions>? configure = null) {
		var socket = new System.Net.WebSockets.ClientWebSocket();
		socket.Options.Proxy = new WebProxy($"http://127.0.0.1:{Port}");
		socket.Options.CollectHttpResponseDetails = true;
		socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
			ValidateServerCertificate(certificate is null ? null : new X509Certificate2(certificate), errors);
		configure?.Invoke(socket.Options);
		return socket;
	}

	/// <summary>The certificate the client was shown by the last HTTPS server it connected to (through the proxy).</summary>
	public X509Certificate2? ServerCertificateSeenByClient { get; private set; }

	/// <summary>Thumbprints of extra certificates this client accepts (for example a test origin's own self-signed certificate).</summary>
	public HashSet<string> AlsoTrust { get; } = new();

	public CertificateAuthority? Authority => _host.Authority;

	public MemoryExchangeLog Log { get; }
	/// <summary>A client that speaks HTTP/1.1.</summary>
	public HttpClient Client { get; }

	/// <summary>A client that asks for HTTP/2 (and falls back to HTTP/1.1 if the server does not offer it), like a browser.</summary>
	public HttpClient Http2Client { get; }
	public int Port => _host.Port;
	public string? CaptureRunDirectory => _host.CaptureRunDirectory;

	/// <summary>How many requests, tunnels and WebSockets are in flight right now.</summary>
	public ProxyActivity Activity => _host.Activity;

	/// <summary>Starts a graceful stop (see <see cref="ProxyHost.StopAsync"/>). Disposing the proxy afterwards is still fine.</summary>
	public Task<ShutdownReport> StopAsync(TimeSpan drainTimeout, CancellationToken skipDrain = default) => _host.StopAsync(drainTimeout, skipDrain);

	/// <summary>Asks the proxy to stop, as Ctrl+C would.</summary>
	public void RequestStop() => _host.RequestStop();

	/// <summary>Waits for a stop request or a limit and then stops (see <see cref="ProxyHost.RunUntilStoppedAsync"/>).</summary>
	public Task<StopResult> RunUntilStoppedAsync(TimeSpan drainTimeout, CancellationToken skipDrain = default, Action<StopReason>? onStopping = null) =>
		_host.RunUntilStoppedAsync(drainTimeout, skipDrain, onStopping);

	/// <summary>Reports when a limit is hit; tests use it to simulate one.</summary>
	public ResourceLimitMonitor Limits => _host.Limits;

	public static async Task<TestProxy> StartAsync(ProxyOptions? options = null, ISessionCapture? capture = null, bool clientTrustsProxyCa = true) {
		var log = new MemoryExchangeLog();
		var host = await ProxyHost.StartAsync(options ?? new ProxyOptions { Port = 0 }, log, capture);
		return new TestProxy(host, log, clientTrustsProxyCa);
	}

	public async ValueTask DisposeAsync() {
		Client.Dispose();
		Http2Client.Dispose();
		await _host.DisposeAsync();
	}
}

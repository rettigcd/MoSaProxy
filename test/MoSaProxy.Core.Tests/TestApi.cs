using System.Net;
using System.Net.Http.Json;

namespace MoSaProxy.Tests;

/// <summary>A proxy with the local API on top of it, and a client that calls the API directly (not through the proxy) with the right token.</summary>
public sealed class TestApi : IAsyncDisposable {
	TestApi(TestProxy proxy, ApiHost api, FakeRootStore rootStore) {
		Proxy = proxy;
		RootStore = rootStore;
		Api = api;
		Client = NewClient(api.Token);
	}

	public TestProxy Proxy { get; }
	public ApiHost Api { get; }

	/// <summary>The stand-in for the Windows trusted-root store that the API looks in to see whether the root certificate is trusted.</summary>
	public FakeRootStore RootStore { get; }

	/// <summary>Calls the API with the correct token.</summary>
	public HttpClient Client { get; }

	/// <summary>A client for the API with the given token header, or none when null.</summary>
	public HttpClient NewClient(string? token) {
		var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{Api.Port}/"), Timeout = TimeSpan.FromSeconds(30) };
		if (token is not null) client.DefaultRequestHeaders.Add(ApiHost.TokenHeader, token);
		return client;
	}

	public static async Task<TestApi> StartAsync(ProxyOptions? options = null) {
		var proxy = await TestProxy.StartAsync(options);
		var rootStore = new FakeRootStore();
		return new TestApi(proxy, await ApiHost.StartAsync(proxy.Host, rootStore: rootStore), rootStore);
	}

	/// <summary>Makes one request through the proxy to the origin and waits until it is in the store.</summary>
	public async Task CaptureOneAsync(TestOrigin origin, string path, int expectedCount) {
		await Proxy.Client.GetAsync(origin.UrlFor(path));
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (Proxy.Exchanges.Count < expectedCount && DateTime.UtcNow < deadline) await Task.Delay(10);
	}

	public async ValueTask DisposeAsync() {
		Client.Dispose();
		await Api.DisposeAsync();
		await Proxy.DisposeAsync();
	}
}

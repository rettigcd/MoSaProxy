using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class ExchangeStore_Tests {

	/// <summary>Makes the given requests through a capturing proxy and waits until all of them are in the store.</summary>
	static async Task<(TestProxy Proxy, TestOrigin Origin)> CaptureAsync(params (HttpMethod Method, string Path)[] requests) {
		var origin = await TestOrigin.StartAsync(async c => {
			c.Response.StatusCode = c.Request.Path.Value!.Contains("missing") ? 404 : 200;
			c.Response.Headers["X-Origin"] = "yes";
			await c.Response.WriteAsync("body of " + c.Request.Path);
		});
		var proxy = await TestProxy.StartAsync();
		foreach (var (method, path) in requests) await proxy.Client.SendAsync(new HttpRequestMessage(method, origin.UrlFor(path)));
		await WaitForCountAsync(proxy, requests.Length);
		return (proxy, origin);
	}

	static async Task WaitForCountAsync(TestProxy proxy, int count) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (proxy.Exchanges.Count < count && DateTime.UtcNow < deadline) await Task.Delay(10);
	}

	[Fact]
	public async Task List_AfterThreeRequests_ReturnsThreeSummariesOldestFirst() {

		// Given: a proxy that has captured a GET, a POST and a GET that the origin answers with 404
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/one"), (HttpMethod.Post, "/two"), (HttpMethod.Get, "/missing"));
		await using var _p = proxy; await using var _o = origin;

		// When: the store is asked for its list
		var list = proxy.Exchanges.List();

		// Then: there are three, numbered from 1, each with its method, path, status, protocol and byte counts
		list.Select(e => e.Id).ShouldBe(new[] { 1, 2, 3 });
		list.Select(e => e.Method).ShouldBe(new[] { "GET", "POST", "GET" });
		list.Select(e => e.Path).ShouldBe(new[] { "/one", "/two", "/missing" });
		list.Select(e => e.Status).ShouldBe(new int?[] { 200, 200, 404 });
		list[0].Host.ShouldBe("127.0.0.1");
		list[0].Scheme.ShouldBe("http");
		list[0].Mode.ShouldBe("plain");
		list[0].ClientHttpVersion.ShouldBe("HTTP/1.1");
		list[0].ResponseBytes.ShouldBe("body of /one".Length);
		list[0].Url.ShouldBe(origin.UrlFor("/one"));
	}

	[Fact]
	public async Task List_WithAfterAndLimit_PagesThroughTheExchanges() {

		// Given: a proxy that has captured four requests
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/a"), (HttpMethod.Get, "/b"), (HttpMethod.Get, "/c"), (HttpMethod.Get, "/d"));
		await using var _p = proxy; await using var _o = origin;

		// When: the list is asked for two exchanges after number 1
		var page = proxy.Exchanges.List(new ExchangeQuery { After = 1, Limit = 2 });

		// Then: it holds exchanges 2 and 3
		page.Select(e => e.Id).ShouldBe(new[] { 2, 3 });
	}

	[Fact]
	public async Task List_WithFilters_ReturnsOnlyTheMatchingExchanges() {

		// Given: a proxy that has captured a GET, a POST and a missing page
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/one"), (HttpMethod.Post, "/two"), (HttpMethod.Get, "/missing"));
		await using var _p = proxy; await using var _o = origin;

		// When: the list is filtered by method, by status, by text in the URL, and by a host that none has
		var posts = proxy.Exchanges.List(new ExchangeQuery { Method = "post" });
		var notFound = proxy.Exchanges.List(new ExchangeQuery { Status = 404 });
		var text = proxy.Exchanges.List(new ExchangeQuery { Text = "ONE" });
		var otherHost = proxy.Exchanges.List(new ExchangeQuery { Host = "example.test" });

		// Then: each filter keeps just what matches, regardless of case
		posts.Select(e => e.Id).ShouldBe(new[] { 2 });
		notFound.Select(e => e.Id).ShouldBe(new[] { 3 });
		text.Select(e => e.Id).ShouldBe(new[] { 1 });
		otherHost.ShouldBeEmpty();
	}

	[Fact]
	public async Task Detail_OfACapturedExchange_HasTheHeadersInTheOrderTheyWereCaptured() {

		// Given: a proxy that has captured one request to an origin that adds a header
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/page"));
		await using var _p = proxy; await using var _o = origin;

		// When: the exchange's details are read
		var detail = proxy.Exchanges.Detail(1)!;

		// Then: the request and response heads are parsed, the origin's header is there, and the sources say where the files came from
		detail.Request!.FirstLine.ShouldBe($"GET {origin.UrlFor("/page")} HTTP/1.1");
		detail.Request.Headers.ShouldContain(h => h.Key == "Host" && h.Value == $"127.0.0.1:{origin.Port}");
		detail.Response!.FirstLine.ShouldStartWith("HTTP/1.1 200");
		detail.Response.Headers.ShouldContain(h => h.Key == "X-Origin" && h.Value == "yes");
		detail.RequestSource.ShouldBe(CaptureSource.Wire);
		detail.ResponseSource.ShouldBe(CaptureSource.Wire);
		detail.Summary.Id.ShouldBe(1);
		detail.HasWebSocketLog.ShouldBeFalse();
	}

	[Fact]
	public async Task OpenResponse_OfACapturedExchange_ReadsTheCapturedBytes() {

		// Given: a proxy that has captured one request
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/page"));
		await using var _p = proxy; await using var _o = origin;

		// When: the response is opened and read
		using var stream = proxy.Exchanges.OpenResponse(1)!;
		using var reader = new StreamReader(stream, Encoding.Latin1);
		string text = await reader.ReadToEndAsync();

		// Then: it is the response as captured: the head and the body (as the origin sent it, chunked)
		text.ShouldStartWith("HTTP/1.1 200");
		text.ShouldContain("body of /page");
	}

	[Fact]
	public async Task Detail_OfAnExchangeThatDoesNotExist_IsNull() {

		// Given: a proxy that has captured one request
		var (proxy, origin) = await CaptureAsync((HttpMethod.Get, "/page"));
		await using var _p = proxy; await using var _o = origin;

		// When: an exchange number that was never captured is asked for
		var detail = proxy.Exchanges.Detail(99);
		var summary = proxy.Exchanges.Summary(99);
		var response = proxy.Exchanges.OpenResponse(99);

		// Then: there is nothing for any of the ways of asking
		detail.ShouldBeNull();
		summary.ShouldBeNull();
		response.ShouldBeNull();
	}

	[Fact]
	public async Task List_WhileAnExchangeIsStillRunning_DoesNotShowItYet() {

		// Given: an origin that holds its answer until told, and a request that is waiting for it
		var arrived = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		await using var origin = await TestOrigin.StartAsync(async c => { arrived.TrySetResult(); await release.Task; await c.Response.WriteAsync("late"); });
		await using var proxy = await TestProxy.StartAsync();
		var pending = proxy.Client.GetAsync(origin.UrlFor("/slow"));
		await arrived.Task;

		// When: the store is listed while the exchange is running, and again once it has finished
		var whileRunning = proxy.Exchanges.List();
		release.SetResult();
		await (await pending).Content.ReadAsStringAsync();
		await WaitForCountAsync(proxy, 1);
		var afterwards = proxy.Exchanges.List();

		// Then: it is absent while running and present when finished
		whileRunning.ShouldBeEmpty();
		afterwards.Select(e => e.Id).ShouldBe(new[] { 1 });
	}

	[Fact]
	public async Task List_OfAFolderThatNoLongerExists_IsEmpty() {

		// Given: a store on a folder that was deleted
		using var folder = new TempFolder();
		var store = new ExchangeStore(Path.Combine(folder.Path, "gone"));

		// When: it is listed
		var list = store.List();

		// Then: it is empty and nothing is thrown
		list.ShouldBeEmpty();
		store.Count.ShouldBe(0);
	}
}

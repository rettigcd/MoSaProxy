using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class Api_Tests {

	static Task<TestOrigin> StartOriginAsync() =>
		TestOrigin.StartAsync(async c => {
			c.Response.StatusCode = c.Request.Path.Value!.Contains("missing") ? 404 : 200;
			c.Response.Headers["X-Origin"] = "yes";
			await c.Response.WriteAsync("body of " + c.Request.Path);
		});

	// ---- safety ---------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Request_WithoutTheToken_IsRefusedWith401() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();
		using var anonymous = api.NewClient(token: null);

		// When: a client asks for the status without a token
		var response = await anonymous.GetAsync("api/status");

		// Then: it is refused as unauthorized
		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Request_WithTheWrongToken_IsRefusedWith401() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();
		using var wrong = api.NewClient("not-the-token");

		// When: a client asks for the status with a different token
		var response = await wrong.GetAsync("api/status");

		// Then: it is refused as unauthorized
		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Request_WithTheTokenInTheQueryString_IsAccepted() {

		// Given: the API running, and a client with no token header (as an event stream has none)
		await using var api = await TestApi.StartAsync();
		using var anonymous = api.NewClient(token: null);

		// When: it asks for the status with the token as a query parameter
		var response = await anonymous.GetAsync($"api/status?token={api.Api.Token}");

		// Then: it is answered
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Request_WithAHostHeaderThatIsNotTheListener_IsRefusedWith403() {

		// Given: the API running, and a request that arrives under another name (DNS rebinding)
		await using var api = await TestApi.StartAsync();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
		request.Headers.Host = $"evil.test:{api.Api.Port}";

		// When: it is sent, with the right token
		var response = await api.Client.SendAsync(request);

		// Then: it is refused as forbidden, token or not
		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Request_WithAnotherSitesOrigin_IsRefusedWith403() {

		// Given: the API running, and a request that a page on another site made (it carries that site as its Origin)
		await using var api = await TestApi.StartAsync();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
		request.Headers.Add("Origin", "https://evil.test");

		// When: it is sent, with the right token
		var response = await api.Client.SendAsync(request);

		// Then: it is refused as forbidden
		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Request_WithTheListenersOwnOrigin_IsAccepted() {

		// Given: the API running, and a request from a page the API itself served (its Origin is the listener's own)
		await using var api = await TestApi.StartAsync();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
		request.Headers.Add("Origin", $"http://127.0.0.1:{api.Api.Port}");

		// When: it is sent, with the right token
		var response = await api.Client.SendAsync(request);

		// Then: it is answered
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	// ---- status and capture ---------------------------------------------------------------------------------------------------

	[Fact]
	public async Task GetStatus_ReportsHowTheProxyIsSetUp() {

		// Given: a proxy with its defaults and the API on top of it
		await using var api = await TestApi.StartAsync();

		// When: the status is read
		var status = await api.Client.GetFromJsonAsync<ApiStatus>("api/status");

		// Then: it names the proxy's port, says interception is off, HTTP/2 on and capture on, and gives the capture folder
		status!.ProxyPort.ShouldBe(api.Proxy.Port);
		status.Intercept.ShouldBeFalse();
		status.Http2.ShouldBeTrue();
		status.CaptureEnabled.ShouldBeTrue();
		status.ExchangesCaptured.ShouldBe(0);
		status.CaptureFolder.ShouldBe(api.Proxy.CaptureRunDirectory);
	}

	[Fact]
	public async Task PutCapture_Off_StopsCapturingAndGetCaptureReportsIt() {

		// Given: the API running over a capturing proxy and an origin
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();

		// When: capture is switched off through the API and a request is made through the proxy
		var put = await api.Client.PutAsJsonAsync("api/capture", new ApiCaptureRequest(false));
		await api.Proxy.Client.GetAsync(origin.UrlFor("/ignored"));
		await api.Proxy.Log.WaitForAsync();
		var state = await api.Client.GetFromJsonAsync<ApiCaptureState>("api/capture");

		// Then: the answer and a later read both say capture is off, the proxy itself is off, and nothing was captured
		put.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await put.Content.ReadFromJsonAsync<ApiCaptureState>())!.Enabled.ShouldBeFalse();
		state!.Enabled.ShouldBeFalse();
		state.ExchangesCaptured.ShouldBe(0);
		api.Proxy.CaptureEnabled.ShouldBeFalse();
	}

	[Fact]
	public async Task PutCapture_OnAgain_CapturesTheNextRequest() {

		// Given: a proxy that started with capture off, with the API over it
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync(new ProxyOptions { Port = 0, CaptureEnabled = false });

		// When: capture is switched on through the API and a request is made
		await api.Client.PutAsJsonAsync("api/capture", new ApiCaptureRequest(true));
		await api.CaptureOneAsync(origin, "/seen", expectedCount: 1);
		var state = await api.Client.GetFromJsonAsync<ApiCaptureState>("api/capture");

		// Then: the request is captured and counted
		state!.Enabled.ShouldBeTrue();
		state.ExchangesCaptured.ShouldBe(1);
	}

	// ---- exchanges ------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task GetExchanges_AfterThreeRequests_ListsThemOldestFirstWithoutANextPage() {

		// Given: three captured requests
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/one", 1);
		await api.CaptureOneAsync(origin, "/two", 2);
		await api.CaptureOneAsync(origin, "/missing", 3);

		// When: the list is read
		var page = await api.Client.GetFromJsonAsync<ApiExchangePage>("api/exchanges");

		// Then: all three come back in order with their paths and statuses, and there is no next page
		page!.Items.Select(e => e.Id).ShouldBe(new[] { 1, 2, 3 });
		page.Items.Select(e => e.Path).ShouldBe(new[] { "/one", "/two", "/missing" });
		page.Items.Select(e => e.Status).ShouldBe(new int?[] { 200, 200, 404 });
		page.Next.ShouldBeNull();
	}

	[Fact]
	public async Task GetExchanges_WithALimit_PagesThroughUsingNext() {

		// Given: three captured requests
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/one", 1);
		await api.CaptureOneAsync(origin, "/two", 2);
		await api.CaptureOneAsync(origin, "/three", 3);

		// When: pages of two are read, the second asked for with the first's next value
		var first = await api.Client.GetFromJsonAsync<ApiExchangePage>("api/exchanges?limit=2");
		var second = await api.Client.GetFromJsonAsync<ApiExchangePage>($"api/exchanges?limit=2&after={first!.Next}");

		// Then: the first holds 1 and 2 and points at 2, and the second holds 3 and is the last
		first.Items.Select(e => e.Id).ShouldBe(new[] { 1, 2 });
		first.Next.ShouldBe(2);
		second!.Items.Select(e => e.Id).ShouldBe(new[] { 3 });
		second.Next.ShouldBeNull();
	}

	[Fact]
	public async Task GetExchanges_WithFilters_ReturnsOnlyTheMatches() {

		// Given: a captured page and a captured missing page
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/page", 1);
		await api.CaptureOneAsync(origin, "/missing", 2);

		// When: the list is filtered by status 404, by text, and by a host nothing used
		var byStatus = await api.Client.GetFromJsonAsync<ApiExchangePage>("api/exchanges?status=404");
		var byText = await api.Client.GetFromJsonAsync<ApiExchangePage>("api/exchanges?q=PAGE");
		var byHost = await api.Client.GetFromJsonAsync<ApiExchangePage>("api/exchanges?host=example.test");

		// Then: each filter keeps just what matches
		byStatus!.Items.Select(e => e.Id).ShouldBe(new[] { 2 });
		byText!.Items.Select(e => e.Id).ShouldBe(new[] { 1 });
		byHost!.Items.ShouldBeEmpty();
	}

	[Theory]
	[InlineData("api/exchanges?limit=0")]
	[InlineData("api/exchanges?limit=5000")]
	[InlineData("api/exchanges?limit=-1")]
	public async Task GetExchanges_WithALimitOutOfRange_Answers400(string url) {

		// Given: the API running
		await using var api = await TestApi.StartAsync();

		// When: the list is asked for with a limit that is too small or too large
		var response = await api.Client.GetAsync(url);

		// Then: the answer is 400
		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task GetExchange_ThatWasCaptured_ReturnsItsDetail() {

		// Given: one captured request to an origin that adds a header
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/page", 1);

		// When: its detail is read
		var detail = await api.Client.GetFromJsonAsync<ExchangeDetail>("api/exchanges/1");

		// Then: it has the summary, the heads with the origin's header, and the sources of the captured bytes
		detail!.Summary.Path.ShouldBe("/page");
		detail.Request!.FirstLine.ShouldBe($"GET {origin.UrlFor("/page")} HTTP/1.1");
		detail.Response!.Headers.ShouldContain(h => h.Key == "X-Origin" && h.Value == "yes");
		detail.RequestSource.ShouldBe(CaptureSource.Wire);
	}

	[Fact]
	public async Task GetExchange_ThatDoesNotExist_Answers404WithAProblemDescription() {

		// Given: the API running with nothing captured
		await using var api = await TestApi.StartAsync();

		// When: exchange 99 is asked for
		var response = await api.Client.GetAsync("api/exchanges/99");

		// Then: the answer is 404 with a problem description that says which exchange
		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
		(await response.Content.ReadAsStringAsync()).ShouldContain("99");
	}
}

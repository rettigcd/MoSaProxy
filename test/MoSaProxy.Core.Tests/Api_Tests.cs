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

	// ---- bytes and bodies -----------------------------------------------------------------------------------------------------

	[Fact]
	public async Task GetRequestAndResponseBytes_ReturnTheCapturedMessages() {

		// Given: one captured request
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/page", 1);

		// When: the request and response bytes are read
		string request = await api.Client.GetStringAsync("api/exchanges/1/request");
		var response = await api.Client.GetAsync("api/exchanges/1/response");
		string responseText = await response.Content.ReadAsStringAsync();

		// Then: they are the heads and bodies as captured, served as raw bytes
		request.ShouldStartWith($"GET {origin.UrlFor("/page")} HTTP/1.1\r\n");
		responseText.ShouldStartWith("HTTP/1.1 200 OK\r\n");
		responseText.ShouldContain("body of /page");
		response.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
	}

	[Fact]
	public async Task GetResponseBody_ReturnsJustTheBodyWithTheOriginsContentType() {

		// Given: an origin that answers with a typed body
		await using var origin = await TestOrigin.StartAsync(async c => {
			c.Response.ContentType = "text/x-test";
			c.Response.ContentLength = 13;
			await c.Response.WriteAsync("just the body");
		});
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/typed", 1);

		// When: the response body is read
		var response = await api.Client.GetAsync("api/exchanges/1/response/body");

		// Then: it is only the body, with the origin's content type
		(await response.Content.ReadAsStringAsync()).ShouldBe("just the body");
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/x-test");
	}

	[Fact]
	public async Task GetResponseBody_OfAGzipResponse_ReturnsItAsCapturedUnlessDecodeIsAskedFor() {

		// Given: an origin that answers with a gzipped body
		await using var origin = await TestOrigin.StartAsync(async c => {
			var zipped = new MemoryStream();
			using (var gzip = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionMode.Compress, leaveOpen: true)) gzip.Write("squeezed text"u8);
			c.Response.Headers.ContentEncoding = "gzip";
			c.Response.ContentType = "text/plain";
			c.Response.ContentLength = zipped.Length;
			await c.Response.Body.WriteAsync(zipped.ToArray());
		});
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/zipped", 1);

		// When: the body is read as captured, and again with decode=true
		var raw = await api.Client.GetByteArrayAsync("api/exchanges/1/response/body");
		var decoded = await api.Client.GetAsync("api/exchanges/1/response/body?decode=true");

		// Then: the first is gzip, the second is the text and says it was decoded
		raw.Take(2).ShouldBe(new byte[] { 0x1f, 0x8b });
		(await decoded.Content.ReadAsStringAsync()).ShouldBe("squeezed text");
		decoded.Headers.GetValues("X-MoSaProxy-Decoded").ShouldBe(new[] { "true" });
	}

	[Fact]
	public async Task GetRequestBody_OfAPost_ReturnsWhatWasSent() {

		// Given: a POST with a body, captured
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.Proxy.Client.PostAsync(origin.UrlFor("/submit"), new StringContent("name=value", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (api.Proxy.Exchanges.Count < 1 && DateTime.UtcNow < deadline) await Task.Delay(10);

		// When: the request body is read
		var response = await api.Client.GetAsync("api/exchanges/1/request/body");

		// Then: it is the form that was posted
		(await response.Content.ReadAsStringAsync()).ShouldBe("name=value");
		response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-www-form-urlencoded");
	}

	[Theory]
	[InlineData("api/exchanges/99/request")]
	[InlineData("api/exchanges/99/response")]
	[InlineData("api/exchanges/99/request/body")]
	[InlineData("api/exchanges/99/response/body")]
	[InlineData("api/exchanges/99/websocket")]
	public async Task GetBytesOrBody_OfAnExchangeThatDoesNotExist_Answers404(string url) {

		// Given: the API running with nothing captured
		await using var api = await TestApi.StartAsync();

		// When: bytes of exchange 99 are asked for
		var response = await api.Client.GetAsync(url);

		// Then: the answer is 404
		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GetWebSocketLog_OfAPlainExchange_Answers404() {

		// Given: a captured ordinary request (no WebSocket)
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/page", 1);

		// When: its WebSocket log is asked for
		var response = await api.Client.GetAsync("api/exchanges/1/websocket");

		// Then: there is none
		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	// ---- export and stop ------------------------------------------------------------------------------------------------------

	static async Task<System.IO.Compression.ZipArchive> ZipOf(HttpResponseMessage response) =>
		new(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));

	[Fact]
	public async Task ExportSaz_WithoutIds_DownloadsAllFinishedExchanges() {

		// Given: two captured requests
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/one", 1);
		await api.CaptureOneAsync(origin, "/two", 2);

		// When: the archive is downloaded
		var response = await api.Client.GetAsync("api/export/saz");
		using var zip = await ZipOf(response);

		// Then: it is a zip named mosaproxy.saz holding both exchanges' request files
		response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
		response.Content.Headers.ContentDisposition!.FileName.ShouldBe("mosaproxy.saz");
		zip.Entries.Select(e => e.FullName).Where(n => n.EndsWith("_c.txt")).ShouldBe(new[] { "raw/1_c.txt", "raw/2_c.txt" });
	}

	[Fact]
	public async Task ExportSaz_WithIds_HoldsOnlyThoseExchanges() {

		// Given: three captured requests
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/one", 1);
		await api.CaptureOneAsync(origin, "/two", 2);
		await api.CaptureOneAsync(origin, "/three", 3);

		// When: exchanges 1 and 3 are asked for (and 99, which does not exist)
		var response = await api.Client.GetAsync("api/export/saz?ids=1,3,99");
		using var zip = await ZipOf(response);

		// Then: the archive has just those two
		zip.Entries.Select(e => e.FullName).Where(n => n.EndsWith("_c.txt")).ShouldBe(new[] { "raw/1_c.txt", "raw/3_c.txt" });
	}

	[Fact]
	public async Task ExportSaz_WithNothingToExport_Answers404() {

		// Given: nothing captured
		await using var api = await TestApi.StartAsync();

		// When: an archive is asked for, all of it and by number
		var all = await api.Client.GetAsync("api/export/saz");
		var some = await api.Client.GetAsync("api/export/saz?ids=5");

		// Then: both are 404
		all.StatusCode.ShouldBe(HttpStatusCode.NotFound);
		some.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ExportSaz_WithAnIdThatIsNotANumber_Answers400() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();

		// When: an archive is asked for with a bad id
		var response = await api.Client.GetAsync("api/export/saz?ids=1,abc");

		// Then: the answer is 400
		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task ExportSaz_LeavesNoTemporaryFileBehind() {

		// Given: a captured request and the temporary files already in the temp folder
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/one", 1);
		var before = Directory.GetFiles(Path.GetTempPath(), "MoSaProxy-export-*.saz").ToHashSet();

		// When: the archive is downloaded in full
		(await api.Client.GetAsync("api/export/saz")).EnsureSuccessStatusCode();
		await Task.Delay(100);

		// Then: no export file is left
		Directory.GetFiles(Path.GetTempPath(), "MoSaProxy-export-*.saz").Except(before).ShouldBeEmpty();
	}

	[Fact]
	public async Task PostStop_AsksTheProxyToStop() {

		// Given: a running proxy that nobody has asked to stop
		await using var api = await TestApi.StartAsync();
		var stopRequested = api.Proxy.Host.WaitForStopRequestAsync();
		stopRequested.IsCompleted.ShouldBeFalse();

		// When: the stop is posted
		var response = await api.Client.PostAsync("api/stop", null);

		// Then: the answer is 202 and the proxy's stop request completes
		response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
		await stopRequested.WaitAsync(TimeSpan.FromSeconds(5));
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

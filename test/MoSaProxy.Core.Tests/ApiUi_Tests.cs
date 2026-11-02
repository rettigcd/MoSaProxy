using System.Net;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>The browser UI is a few static files served by the API's listener. These tests check what is served, and what is not.</summary>
public class ApiUi_Tests {

	[Fact]
	public async Task Root_WithTheToken_ServesThePageWithALockedDownPolicy() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();

		// When: the root is asked for with the token
		var response = await api.Client.GetAsync("");
		string page = await response.Content.ReadAsStringAsync();

		// Then: it is the HTML page, never cached, allowed to load only its own script and style
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
		page.ShouldContain("<script src=\"app.js\">");
		response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self'");
		response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
		response.Headers.CacheControl!.NoStore.ShouldBeTrue();
	}

	[Fact]
	public async Task Root_WithoutTheToken_IsRefused() {

		// Given: the API running, and a client that has no token
		await using var api = await TestApi.StartAsync();
		using var anonymous = api.NewClient(token: null);

		// When: the page is asked for
		var response = await anonymous.GetAsync("");

		// Then: it is refused, so the page cannot be loaded by anything that was not given the address
		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Theory]
	[InlineData("app.js", "text/javascript")]
	[InlineData("app.css", "text/css")]
	public async Task ScriptAndStyle_WithoutTheToken_AreServed(string path, string contentType) {

		// Given: the API running, and a client with no token (a <script src> cannot carry one)
		await using var api = await TestApi.StartAsync();
		using var anonymous = api.NewClient(token: null);

		// When: the file is asked for
		var response = await anonymous.GetAsync(path);

		// Then: it is served with the right type
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe(contentType);
		(await response.Content.ReadAsStringAsync()).ShouldNotBeEmpty();
	}

	[Fact]
	public async Task ScriptAndStyle_UnderAnotherHostName_AreStillRefused() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();
		using var request = new HttpRequestMessage(HttpMethod.Get, "app.js");
		request.Headers.Host = $"evil.test:{api.Api.Port}";

		// When: the script is asked for under a name that is not the listener's
		var response = await api.Client.SendAsync(request);

		// Then: it is refused like everything else (DNS rebinding)
		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Theory]
	[InlineData("ApiHost.cs")]
	[InlineData("core/Api/ApiHost.cs")]
	[InlineData("ui/app.js")]
	[InlineData("index.html")]
	[InlineData("appsettings.json")]
	[InlineData("MoSaProxy.Core.dll")]
	[InlineData("..%2f..%2fappsettings.json")]
	[InlineData("%2e%2e/%2e%2e/Windows/win.ini")]
	public async Task OtherPaths_EvenWithTheToken_AreNotFound(string path) {

		// Given: the API running
		await using var api = await TestApi.StartAsync();

		// When: something that is not one of the UI's files or an API endpoint is asked for
		var response = await api.Client.GetAsync(path);

		// Then: there is nothing there
		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Page_CannotBePostedTo() {

		// Given: the API running
		await using var api = await TestApi.StartAsync();

		// When: a file is posted to
		var response = await api.Client.PostAsync("app.js", new StringContent("x"));

		// Then: the method is refused
		response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
	}

	[Fact]
	public void Assembly_HoldsExactlyTheThreeUiFiles() {

		// Given: the files compiled into the assembly under ui/
		var names = typeof(ApiHost).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("ui/")).Order().ToList();

		// When/Then: they are the page, its script and its style, which are the only files the listener serves
		names.ShouldBe(new[] { "ui/app.css", "ui/app.js", "ui/index.html" });
	}

	[Fact]
	public async Task Page_LoadsNothingFromElsewhereAndHasNoInlineScript() {

		// Given: the page and its script as served
		await using var api = await TestApi.StartAsync();
		string page = await api.Client.GetStringAsync("");
		string script = await api.Client.GetStringAsync("app.js");

		// When/Then: the page names no other site, has no inline script, and the script never writes captured text as HTML
		page.ShouldNotContain("http://");
		page.ShouldNotContain("https://");
		page.ShouldNotContain("<script>");
		page.ShouldNotContain(" onclick=");
		script.ShouldNotContain("innerHTML");
		script.ShouldNotContain("outerHTML");
		script.ShouldNotContain("insertAdjacentHTML");
		script.ShouldNotContain("eval(");
	}
}

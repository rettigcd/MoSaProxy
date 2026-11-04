using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MoSaProxy.Tests;

public class ApiIntercept_Tests {

	static ProxyOptions Plain(TempFolder caFolder) => new() { Port = 0, CaDirectory = caFolder.Path };

	[Fact]
	public async Task GetIntercept_ForAProxyStartedWithoutTheOption_SaysOffAndNothingAboutTheCertificate() {

		// Given: the API over a proxy that never intercepted
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));

		// When: the interception state is read
		var state = await api.Client.GetFromJsonAsync<ApiInterceptState>("api/intercept");

		// Then: it is off, nothing is skipped, and the certificate fields are empty (it does not exist yet)
		state!.Enabled.ShouldBeFalse();
		state.Bypass.ShouldBeEmpty();
		state.CaTrusted.ShouldBeNull();
		state.CaSubject.ShouldBeNull();
		state.CaFile.ShouldBeNull();
	}

	[Fact]
	public async Task PutIntercept_On_SwitchesTheProxyAndNamesTheCertificate() {

		// Given: the API over a proxy that never intercepted
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));

		// When: interception is switched on through the API
		var response = await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, null));
		var state = await response.Content.ReadFromJsonAsync<ApiInterceptState>();

		// Then: the proxy itself is on, and the answer names the certificate and its file, which exists
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		api.Proxy.Host.InterceptEnabled.ShouldBeTrue();
		state!.Enabled.ShouldBeTrue();
		state.CaSubject.ShouldBe(CertificateAuthority.RootSubject);
		File.Exists(state.CaFile).ShouldBeTrue();
		(await api.Client.GetFromJsonAsync<ApiInterceptState>("api/intercept"))!.Enabled.ShouldBeTrue();
	}

	[Fact]
	public async Task GetIntercept_ReportsWhetherTheRootCertificateIsTrusted() {

		// Given: interception switched on, with the root not installed anywhere
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));
		await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, null));
		var untrusted = await api.Client.GetFromJsonAsync<ApiInterceptState>("api/intercept");

		// When: the root is installed in the (fake) trust store and the state is read again
		var root = api.Proxy.Authority!.Root;
		api.RootStore.Preinstall(root.Thumbprint, root.Subject);
		var trusted = await api.Client.GetFromJsonAsync<ApiInterceptState>("api/intercept");

		// Then: it said not trusted, then trusted
		untrusted!.CaTrusted.ShouldBe(false);
		trusted!.CaTrusted.ShouldBe(true);
	}

	[Fact]
	public async Task GetIntercept_WhenTheTrustStoreCannotBeRead_SaysTrustIsNotKnown() {

		// Given: interception on, and a trust store that fails when read
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));
		await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, null));
		api.RootStore.ReadFailure = new UnauthorizedAccessException("no access");

		// When: the state is read
		var state = await api.Client.GetFromJsonAsync<ApiInterceptState>("api/intercept");

		// Then: it still answers, with trust unknown
		state!.Enabled.ShouldBeTrue();
		state.CaTrusted.ShouldBeNull();
	}

	[Fact]
	public async Task PutIntercept_WithOnlyTheBypassList_ChangesTheListAndLeavesTheSwitchAlone() {

		// Given: the API over a proxy that intercepts
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(new ProxyOptions { Port = 0, Intercept = true, CaDirectory = caFolder.Path });

		// When: only the skip list is sent, with a repeat and spaces
		var response = await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(null, ["bank.example", " bank.example ", "*.secure.example"]));
		var state = await response.Content.ReadFromJsonAsync<ApiInterceptState>();

		// Then: the list is set (tidied) and interception is still on
		state!.Bypass.ShouldBe(new[] { "bank.example", "*.secure.example" });
		state.Enabled.ShouldBeTrue();
		api.Proxy.Host.InterceptBypass.ShouldBe(state.Bypass);
	}

	[Fact]
	public async Task PutIntercept_WithABadHostPattern_Answers400AndChangesNothing() {

		// Given: the API over a proxy with interception off
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));

		// When: interception is asked to switch on together with a bad skip entry
		var response = await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, ["fine.example", "http://nope"]));

		// Then: 400, and neither part took effect
		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).ShouldContain("http://nope");
		api.Proxy.Host.InterceptEnabled.ShouldBeFalse();
		api.Proxy.Host.InterceptBypass.ShouldBeEmpty();
		api.Proxy.Authority.ShouldBeNull();
	}

	[Fact]
	public async Task PutIntercept_WhenTheCertificateCannotBeMade_Answers409AndChangesNothing() {

		// Given: a proxy whose CA folder cannot be created because a file is in the way
		using var caFolder = new TempFolder();
		string blocked = Path.Combine(caFolder.Path, "in-the-way");
		File.WriteAllText(blocked, "not a folder");
		await using var api = await TestApi.StartAsync(new ProxyOptions { Port = 0, CaDirectory = blocked });

		// When: interception is asked to switch on, together with a skip list
		var response = await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, ["a.example"]));

		// Then: 409 saying why; interception is off and the list was not applied
		response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).ShouldContain(blocked.Replace("\\", "\\\\"));
		api.Proxy.Host.InterceptEnabled.ShouldBeFalse();
		api.Proxy.Host.InterceptBypass.ShouldBeEmpty();
	}

	[Fact]
	public async Task GetStatus_FollowsTheLiveInterceptionSetting() {

		// Given: the API over a proxy started without interception
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));

		// When: interception is switched on and the status is read
		await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, null));
		var status = await api.Client.GetFromJsonAsync<ApiStatus>("api/status");

		// Then: the status says it is on, though the option said off
		status!.Intercept.ShouldBeTrue();
	}

	[Fact]
	public async Task Stream_AnnouncesInterceptionAndSkipListChanges() {

		// Given: an open event stream
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));
		using var stream = await SseReader.OpenAsync(api.Client, "api/stream");

		// When: interception is switched on and the skip list changed
		await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(true, null));
		var on = await stream.NextAsync();
		await api.Client.PutAsJsonAsync("api/intercept", new ApiInterceptRequest(null, ["a.example", "*.b.example"]));
		var bypass = await stream.NextAsync();

		// Then: both arrive, with their data
		on.Name.ShouldBe("intercept");
		on.Data.ShouldBe("{\"enabled\":true}");
		bypass.Name.ShouldBe("bypass");
		bypass.Data.ShouldBe("{\"hosts\":[\"a.example\",\"*.b.example\"]}");
	}

	[Theory]
	[InlineData("GET")]
	[InlineData("PUT")]
	public async Task Intercept_WithoutTheToken_IsRefused(string method) {

		// Given: the API running, and a client that has no token
		using var caFolder = new TempFolder();
		await using var api = await TestApi.StartAsync(Plain(caFolder));
		using var anonymous = api.NewClient(token: null);
		using var request = new HttpRequestMessage(new HttpMethod(method), "api/intercept");
		if (method == "PUT") request.Content = JsonContent.Create(new ApiInterceptRequest(true, null));

		// When: the endpoint is called
		var response = await anonymous.SendAsync(request);

		// Then: it is refused, and the setting is unchanged
		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		api.Proxy.Host.InterceptEnabled.ShouldBeFalse();
	}
}

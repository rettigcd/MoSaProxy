using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>Interception can be switched on and off, and its list of skipped hosts changed, while the proxy runs.</summary>
public class InterceptSwitch_Tests {

	/// <summary>A proxy that starts with interception off (but knows where its CA folder is) and accepts the fake origin's self-signed certificate.</summary>
	static ProxyOptions Plain(TempFolder caFolder, bool intercept = false) => new() {
		Port = 0,
		Intercept = intercept,
		CaDirectory = caFolder.Path,
		OriginCertificateValidation = (_, _, _, _) => true,
	};

	/// <summary>Requests a page and closes the tunnel afterwards, so that the next request opens a new one and meets the setting as it is then.</summary>
	static async Task GetOnFreshTunnelAsync(TestProxy proxy, string url) {
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.ConnectionClose = true;
		using var response = await proxy.Client.SendAsync(request);
		await response.Content.ReadAsStringAsync();
	}

	static bool WasDecrypted(TestProxy proxy) => proxy.ServerCertificateSeenByClient!.Issuer == proxy.Authority!.Root.Subject;

	[Fact]
	public async Task Start_WithoutTheOption_InterceptionIsOffAndThereIsNoAuthorityYet() {

		// Given/When: a proxy started without --intercept
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder));

		// Then: interception is off, nothing skipped, and no root certificate has been made
		proxy.Host.InterceptEnabled.ShouldBeFalse();
		proxy.Host.InterceptBypass.ShouldBeEmpty();
		proxy.Authority.ShouldBeNull();
		Directory.GetFiles(caFolder.Path).ShouldBeEmpty();
	}

	[Fact]
	public async Task SetInterceptEnabled_OnAndOff_ChangesWhatTheNextTunnelDoes() {

		// Given: an HTTPS origin and a proxy that starts without interception
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: a page is requested, interception is switched on and it is requested again, then it is switched off and requested again
		await GetOnFreshTunnelAsync(proxy, origin.UrlFor("/1"));
		string beforeThumbprint = proxy.ServerCertificateSeenByClient!.Thumbprint;
		proxy.Host.SetInterceptEnabled(true);
		await GetOnFreshTunnelAsync(proxy, origin.UrlFor("/2"));
		bool decryptedWhileOn = WasDecrypted(proxy);
		proxy.Host.SetInterceptEnabled(false);
		await GetOnFreshTunnelAsync(proxy, origin.UrlFor("/3"));

		// Then: the first and last went through blind tunnels (the client saw the origin's own certificate); the middle one was decrypted
		beforeThumbprint.ShouldBe(origin.Certificate.Thumbprint);
		decryptedWhileOn.ShouldBeTrue();
		proxy.ServerCertificateSeenByClient!.Thumbprint.ShouldBe(origin.Certificate.Thumbprint);
		origin.Seen.Count.ShouldBe(3);
	}

	[Fact]
	public async Task SetInterceptEnabled_WhileATunnelIsOpen_LeavesThatTunnelAsItWas() {

		// Given: a proxy that intercepts, and a tunnel opened and used once (the client keeps it open for reuse)
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder, intercept: true));
		await proxy.Client.GetStringAsync(origin.UrlFor("/first"));

		// When: interception is switched off and the client uses the same tunnel again
		proxy.Host.SetInterceptEnabled(false);
		await proxy.Client.GetStringAsync(origin.UrlFor("/second"));

		// Then: both requests were decrypted inside the one tunnel (one CONNECT, two requests logged)
		var records = await proxy.Log.WaitForAsync(3);
		records.Count(r => r.Method == "CONNECT").ShouldBe(1);
		records.Where(r => r.Method == "GET").Select(r => r.Url).ShouldBe(new[] { origin.UrlFor("/first"), origin.UrlFor("/second") }, ignoreOrder: true);
	}

	[Fact]
	public async Task SetInterceptEnabled_TheFirstTime_MakesTheRootCertificateAndKeepsItAfterwards() {

		// Given: a proxy that has never intercepted, so has no root certificate
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder));

		// When: interception is switched on, off and on again
		proxy.Host.SetInterceptEnabled(true);
		var first = proxy.Authority;
		proxy.Host.SetInterceptEnabled(false);
		proxy.Host.SetInterceptEnabled(true);

		// Then: the certificate was made in the CA folder on the first switch-on, and the same one is used afterwards
		first.ShouldNotBeNull();
		File.Exists(first.RootCertificatePath).ShouldBeTrue();
		proxy.Authority.ShouldBeSameAs(first);
		proxy.Host.InterceptEnabled.ShouldBeTrue();
	}

	[Fact]
	public async Task SetInterceptEnabled_WhenTheRootCertificateCannotBeMade_ThrowsAndStaysOff() {

		// Given: a proxy whose CA folder cannot be created because a file is in the way
		using var caFolder = new TempFolder();
		string blocked = Path.Combine(caFolder.Path, "in-the-way");
		File.WriteAllText(blocked, "not a folder");
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaDirectory = blocked });

		// When: interception is switched on
		var failure = Should.Throw<InvalidOperationException>(() => proxy.Host.SetInterceptEnabled(true));

		// Then: it says why, and interception is still off
		failure.Message.ShouldContain(blocked);
		proxy.Host.InterceptEnabled.ShouldBeFalse();
		proxy.Authority.ShouldBeNull();
	}

	[Fact]
	public async Task SetInterceptBypass_ChangesWhichHostsAreSkippedForTheNextTunnel() {

		// Given: a proxy that intercepts
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder, intercept: true));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: 127.0.0.1 is put on the skip list and requested, then taken off and requested again
		proxy.Host.SetInterceptBypass(["127.0.0.1"]);
		await GetOnFreshTunnelAsync(proxy, origin.UrlFor("/skipped"));
		bool decryptedWhileSkipped = WasDecrypted(proxy);
		proxy.Host.SetInterceptBypass([]);
		await GetOnFreshTunnelAsync(proxy, origin.UrlFor("/not-skipped"));

		// Then: the first was a blind tunnel and the second was decrypted
		decryptedWhileSkipped.ShouldBeFalse();
		WasDecrypted(proxy).ShouldBeTrue();
	}

	[Fact]
	public async Task SetInterceptBypass_TrimsDropsBlanksAndRepeats_AndRefusesBadEntriesWithoutChangingAnything() {

		// Given: a proxy with one host skipped
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder));
		proxy.Host.SetInterceptBypass(["keep.example"]);

		// When: a list with spaces, a blank and a repeat is set, and then one with a bad entry
		proxy.Host.SetInterceptBypass(["  a.example ", "", "A.EXAMPLE", "*.b.example"]);
		var cleaned = proxy.Host.InterceptBypass.ToList();
		Should.Throw<ArgumentException>(() => proxy.Host.SetInterceptBypass(["fine.example", "not a host/"]));

		// Then: the first was tidied, and the bad one left the list as it was
		cleaned.ShouldBe(new[] { "a.example", "*.b.example" });
		proxy.Host.InterceptBypass.ShouldBe(cleaned);
	}

	[Theory]
	[InlineData("example.com", true)]
	[InlineData("*.example.com", true)]
	[InlineData("127.0.0.1", true)]
	[InlineData("[::1]", true)]
	[InlineData("host_name-1.local", true)]
	[InlineData("", false)]
	[InlineData("*.", false)]
	[InlineData("*", false)]
	[InlineData("a*.example.com", false)]
	[InlineData("two words", false)]
	[InlineData("http://example.com", false)]
	[InlineData("example.com/path", false)]
	public void IsValidBypassPattern_Pattern_IsAcceptedOrNot(string pattern, bool expected) {

		// Given/When: a pattern is checked
		bool valid = ProxyHost.IsValidBypassPattern(pattern);

		// Then: only host names, addresses and *.suffix are accepted
		valid.ShouldBe(expected);
	}

	[Fact]
	public async Task Events_OnlyARealChangeIsAnnounced() {

		// Given: a proxy with its interception events watched
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Plain(caFolder));
		var seen = new ConcurrentQueue<string>();
		proxy.Events.InterceptChanged += on => seen.Enqueue("intercept " + on);
		proxy.Events.BypassChanged += hosts => seen.Enqueue("bypass " + string.Join(",", hosts));

		// When: interception is switched off (already off), on, on again, off; and the list is set, set to the same, and cleared
		proxy.Host.SetInterceptEnabled(false);
		proxy.Host.SetInterceptEnabled(true);
		proxy.Host.SetInterceptEnabled(true);
		proxy.Host.SetInterceptEnabled(false);
		proxy.Host.SetInterceptBypass(["a.example"]);
		proxy.Host.SetInterceptBypass(["a.example"]);
		proxy.Host.SetInterceptBypass([]);

		// Then: each real change was announced once, in order
		seen.ToArray().ShouldBe(new[] { "intercept True", "intercept False", "bypass a.example", "bypass " });
	}
}

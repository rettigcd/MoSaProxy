using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>Capture can be switched on and off while the proxy runs; forwarding and the log are not affected.</summary>
public class CaptureSwitch_Tests {

	static ProxyOptions In(TempFolder folder, bool captureEnabled = true) =>
		new() { Port = 0, CaptureDirectory = folder.Path, CaptureEnabled = captureEnabled };

	static string[] CapturedFiles(TestProxy proxy) =>
		Directory.GetFiles(proxy.CaptureRunDirectory!).Select(Path.GetFileName).Where(n => n!.EndsWith("_c.txt")).Order().ToArray()!;

	[Fact]
	public async Task Get_CaptureOffFromTheStart_ForwardsAndLogsButWritesNoCaptureFiles() {

		// Given: a proxy that starts with capture off, and an origin
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(In(folder, captureEnabled: false));

		// When: a client requests a page through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/page"));
		var records = await proxy.Log.WaitForAsync();

		// Then: the client got the page and the exchange was logged, but nothing was captured
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		records.Count.ShouldBe(1);
		CapturedFiles(proxy).ShouldBeEmpty();
	}

	[Fact]
	public async Task Get_CaptureSwitchedOnLater_CapturesTheNextExchangeAsNumberOne() {

		// Given: a proxy with capture off that has already forwarded one request
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(In(folder, captureEnabled: false));
		await proxy.Client.GetAsync(origin.UrlFor("/before"));
		await proxy.Log.WaitForAsync(1);

		// When: capture is switched on and another request is made
		proxy.CaptureEnabled = true;
		await proxy.Client.GetAsync(origin.UrlFor("/after"));
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));

		// Then: only the second request was captured, and it is exchange 1
		CapturedFiles(proxy).ShouldBe(new[] { "1_c.txt" });
		File.ReadAllText(Path.Combine(proxy.CaptureRunDirectory!, "1_c.txt")).ShouldContain("/after");
	}

	[Fact]
	public async Task Get_CaptureSwitchedOffAndOnAgain_SkipsTheMiddleExchangeAndKeepsNumbering() {

		// Given: a proxy capturing, that has captured one request
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(In(folder));
		await proxy.Client.GetAsync(origin.UrlFor("/one"));
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));

		// When: capture is switched off for a second request and on again for a third
		proxy.CaptureEnabled = false;
		await proxy.Client.GetAsync(origin.UrlFor("/two"));
		await proxy.Log.WaitForAsync(2);
		proxy.CaptureEnabled = true;
		await proxy.Client.GetAsync(origin.UrlFor("/three"));
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));

		// Then: the files are 1 and 2 with no gap, and they hold the first and third requests
		CapturedFiles(proxy).ShouldBe(new[] { "1_c.txt", "2_c.txt" });
		File.ReadAllText(Path.Combine(proxy.CaptureRunDirectory!, "1_c.txt")).ShouldContain("/one");
		File.ReadAllText(Path.Combine(proxy.CaptureRunDirectory!, "2_c.txt")).ShouldContain("/three");
	}

	[Fact]
	public async Task Get_CaptureSwitchedOffWhileAnExchangeRuns_StillCapturesThatExchangeCompletely() {

		// Given: an origin that holds its answer until told, and a proxy capturing a request that is waiting for it
		using var folder = new TempFolder();
		var arrived = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		await using var origin = await TestOrigin.StartAsync(async c => { arrived.TrySetResult(); await release.Task; await c.Response.WriteAsync("slow answer"); });
		await using var proxy = await TestProxy.StartAsync(In(folder));
		var pending = proxy.Client.GetAsync(origin.UrlFor("/slow"));
		await arrived.Task;

		// When: capture is switched off and then the origin answers
		proxy.CaptureEnabled = false;
		release.SetResult();
		await (await pending).Content.ReadAsStringAsync();
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));

		// Then: the exchange that began while capture was on has its request, its whole response and its metadata
		File.ReadAllText(Path.Combine(proxy.CaptureRunDirectory!, "1_c.txt")).ShouldContain("/slow");
		File.ReadAllText(Path.Combine(proxy.CaptureRunDirectory!, "1_s.txt")).ShouldContain("slow answer");
	}

	[Fact]
	public async Task Get_CaptureSwitchedOnWhileAnExchangeRuns_DoesNotCaptureThatExchange() {

		// Given: an origin that holds its answer until told, and a proxy with capture off whose request is waiting for it
		using var folder = new TempFolder();
		var arrived = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		await using var origin = await TestOrigin.StartAsync(async c => { arrived.TrySetResult(); await release.Task; await c.Response.WriteAsync("slow answer"); });
		await using var proxy = await TestProxy.StartAsync(In(folder, captureEnabled: false));
		var pending = proxy.Client.GetAsync(origin.UrlFor("/slow"));
		await arrived.Task;

		// When: capture is switched on and then the origin answers
		proxy.CaptureEnabled = true;
		release.SetResult();
		await (await pending).Content.ReadAsStringAsync();
		await proxy.Log.WaitForAsync();

		// Then: the exchange is not captured, not even in part
		CapturedFiles(proxy).ShouldBeEmpty();
		Directory.GetFiles(proxy.CaptureRunDirectory!).ShouldBeEmpty();
	}
}

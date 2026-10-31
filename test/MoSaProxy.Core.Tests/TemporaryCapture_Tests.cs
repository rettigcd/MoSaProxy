using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>What the proxy does with its capture folder when no folder is chosen: always capture, into the temp folder, and clean up.</summary>
public class TemporaryCapture_Tests {

	[Fact]
	public async Task Start_WithNoCaptureFolderChosen_CapturesIntoItsOwnFolderUnderTheTempFolder() {

		// Given: an origin and a proxy started with the default options, which name no capture folder
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();

		// When: a client requests a page through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/page"));

		// Then: the exchange's files are in a folder directly inside %TEMP%\MoSaProxyCaptures
		string run = proxy.CaptureRunDirectory!;
		Path.GetDirectoryName(run).ShouldBe(CaptureRun.DefaultRoot);
		await TempFolder.WaitForFileAsync(Path.Combine(run, "1_m.xml"));
		File.ReadAllText(Path.Combine(run, "1_c.txt")).ShouldStartWith("GET ");
	}

	[Fact]
	public async Task Dispose_WithNoCaptureFolderChosen_DeletesTheRunFolder() {

		// Given: a proxy with the default options (using its own empty temporary root) that has captured one exchange
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		using var root = new TempFolder();
		var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, TemporaryCaptureRoot = root.Path });
		await proxy.Client.GetAsync(origin.UrlFor("/page"));
		string run = proxy.CaptureRunDirectory!;
		await TempFolder.WaitForFileAsync(Path.Combine(run, "1_m.xml"));

		// When: the proxy is disposed
		await proxy.DisposeAsync();

		// Then: the run's capture folder no longer exists
		Directory.Exists(run).ShouldBeFalse();
	}

	[Fact]
	public async Task Dispose_WithACaptureFolderChosenByTheCaller_LeavesItsFilesAlone() {

		// Given: a proxy told to capture into a folder the caller manages, with one exchange captured
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureDirectory = folder.Path });
		await proxy.Client.GetAsync(origin.UrlFor("/page"));
		string run = proxy.CaptureRunDirectory!;
		await TempFolder.WaitForFileAsync(Path.Combine(run, "1_m.xml"));

		// When: the proxy is disposed
		await proxy.DisposeAsync();

		// Then: the files are still there, because the caller owns that folder
		File.Exists(Path.Combine(run, "1_c.txt")).ShouldBeTrue();
	}

	[Fact]
	public async Task Start_PortAlreadyInUse_FailsWithoutLeavingARunFolderBehind() {

		// Given: a proxy already listening on a port, and a second proxy's temporary root that is empty
		using var root = new TempFolder();
		await using var first = await TestProxy.StartAsync();

		// When: a second proxy, using that root, tries to listen on the same port
		var second = async () => await TestProxy.StartAsync(new ProxyOptions { Port = first.Port, TemporaryCaptureRoot = root.Path });

		// Then: starting fails, and the run folder it made for itself is gone again
		await Should.ThrowAsync<Exception>(second);
		Directory.GetDirectories(root.Path).ShouldBeEmpty();
	}
}

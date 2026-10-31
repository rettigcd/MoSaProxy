using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class CaptureEvents_Tests {

	static async Task WaitForAsync(Func<bool> condition) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
	}

	[Fact]
	public async Task Get_CapturedExchange_AnnouncesStartThenFinish() {

		// Given: a proxy whose events are being recorded, and an origin
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();
		var seen = new ConcurrentQueue<string>();
		proxy.Events.ExchangeStarted += n => seen.Enqueue($"started {n}");
		proxy.Events.ExchangeFinished += n => seen.Enqueue($"finished {n}");

		// When: a client makes one request through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/page"));
		await WaitForAsync(() => seen.Count >= 2);

		// Then: the exchange is announced as started and then as finished, with the same number
		seen.ToArray().ShouldBe(new[] { "started 1", "finished 1" });
	}

	[Fact]
	public async Task Finished_WhenAnnounced_TheExchangeCanAlreadyBeReadFromTheStore() {

		// Given: a proxy whose finished event looks the exchange up in the store
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();
		ExchangeSummary? found = null;
		var done = new TaskCompletionSource();
		proxy.Events.ExchangeFinished += n => { found = proxy.Exchanges.Summary(n); done.TrySetResult(); };

		// When: a client makes one request through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/page"));
		await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

		// Then: the store already had the finished exchange
		found.ShouldNotBeNull();
		found.Path.ShouldBe("/page");
	}

	[Fact]
	public async Task Get_WithCaptureOff_AnnouncesNothing() {

		// Given: a proxy that starts with capture off, and a recording of its events
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureEnabled = false });
		var seen = new ConcurrentQueue<int>();
		proxy.Events.ExchangeStarted += seen.Enqueue;
		proxy.Events.ExchangeFinished += seen.Enqueue;

		// When: a client makes a request and the proxy has logged it
		await proxy.Client.GetAsync(origin.UrlFor("/page"));
		await proxy.Log.WaitForAsync();

		// Then: no exchange was announced, because none was captured
		seen.ShouldBeEmpty();
	}

	[Fact]
	public async Task CaptureEnabled_Switched_AnnouncesEachRealChangeOnly() {

		// Given: a proxy with capture on, and a recording of the capture switch events
		await using var proxy = await TestProxy.StartAsync();
		var seen = new List<bool>();
		proxy.Events.CaptureEnabledChanged += seen.Add;

		// When: capture is switched off, switched off again, and switched on
		proxy.CaptureEnabled = false;
		proxy.CaptureEnabled = false;
		proxy.CaptureEnabled = true;

		// Then: only the two real changes were announced
		seen.ShouldBe(new[] { false, true });
	}

	[Fact]
	public async Task Handler_ThatThrows_DoesNotBreakForwardingOrStopOtherHandlers() {

		// Given: a proxy with one event handler that always throws and another that records
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();
		var seen = new ConcurrentQueue<int>();
		proxy.Events.ExchangeFinished += _ => throw new InvalidOperationException("a broken subscriber");
		proxy.Events.ExchangeFinished += seen.Enqueue;

		// When: a client makes a request through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/page"));
		await WaitForAsync(() => !seen.IsEmpty);

		// Then: the client still got its answer and the other handler was still called
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		seen.ToArray().ShouldBe(new[] { 1 });
	}

	[Fact]
	public async Task WebSocket_Messages_AreAnnouncedInBothDirections() {

		// Given: an intercepting proxy, an HTTPS origin that echoes WebSocket messages, and a recording of the message events
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => {
			using var socket = await c.WebSockets.AcceptWebSocketAsync();
			var buffer = new byte[1024];
			var received = await socket.ReceiveAsync(buffer, c.RequestAborted);
			await socket.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, true, c.RequestAborted);
			await socket.ReceiveAsync(buffer, c.RequestAborted);
		}, https: true);
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions {
			Port = 0, Intercept = true, CaDirectory = caFolder.Path, OriginCertificateValidation = (_, _, _, _) => true,
		});
		var seen = new ConcurrentQueue<WebSocketMessageInfo>();
		proxy.Events.WebSocketMessage += seen.Enqueue;
		using var client = proxy.CreateWebSocket();

		// When: the client connects through the proxy, sends "hello" and reads the echo
		await client.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("https://", "wss://")), CancellationToken.None);
		await client.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
		await client.ReceiveAsync(new byte[1024], CancellationToken.None);
		await WaitForAsync(() => seen.Count >= 2);

		// Then: one message from the client and one from the origin were announced, five bytes each, in the same exchange
		var messages = seen.ToArray();
		messages.Select(m => m.FromClient).Order().ToArray().ShouldBe(new[] { false, true });
		messages.ShouldAllBe(m => m.TotalBytes == 5 && m.Type == WebSocketMessageType.Text);
		messages.Select(m => m.Number).Distinct().Count().ShouldBe(1);
	}
}

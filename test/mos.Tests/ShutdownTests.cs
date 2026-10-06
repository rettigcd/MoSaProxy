using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace mos.Tests;

public class ShutdownTests {

	static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000) {
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
		while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
		condition().ShouldBeTrue("the condition was not reached in time");
	}

	static async Task EchoWebSocket(HttpContext context) {
		using var socket = await context.WebSockets.AcceptWebSocketAsync();
		var buffer = new byte[4096];
		while (true) {
			var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
			if (received.MessageType == WebSocketMessageType.Close) {
				await socket.CloseAsync(socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, socket.CloseStatusDescription, CancellationToken.None);
				return;
			}
			await socket.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, context.RequestAborted);
		}
	}

	[Fact]
	public async Task Stop_NothingInFlight_ReturnsAtOnceAndNoNewConnectionsAreAccepted() {

		// Given: a running proxy with nothing going on
		await using var proxy = await TestProxy.StartAsync();
		var timer = Stopwatch.StartNew();

		// When: it is asked to stop with a 10 second drain period
		var report = await proxy.StopAsync(TimeSpan.FromSeconds(10));
		timer.Stop();

		// Then: it stops straight away without aborting anything, and its port no longer accepts connections
		report.Drained.ShouldBeTrue();
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
		using var late = new TcpClient();
		await Should.ThrowAsync<SocketException>(() => late.ConnectAsync("127.0.0.1", proxy.Port));
	}

	[Fact]
	public async Task Stop_RequestInProgress_IsAllowedToFinish() {

		// Given: an origin that takes about a second to answer, and a request to it that is under way
		await using var origin = await TestOrigin.StartAsync(async c => { await Task.Delay(1000); await c.Response.WriteAsync("done"); });
		await using var proxy = await TestProxy.StartAsync();
		var request = proxy.Client.GetAsync(origin.UrlFor("/slow"));
		await WaitUntilAsync(() => proxy.Activity.Exchanges == 1);

		// When: the proxy is asked to stop with a 10 second drain period
		var timer = Stopwatch.StartNew();
		var report = await proxy.StopAsync(TimeSpan.FromSeconds(10));
		timer.Stop();
		var response = await request;

		// Then: the request completed normally, the stop waited for it (and not for the whole drain period), and nothing had to be aborted
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).ShouldBe("done");
		report.Drained.ShouldBeTrue();
		timer.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(500));
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
	}

	[Fact]
	public async Task Stop_RequestOutlastsTheDrainPeriod_IsAbortedReportedAndStillLogged() {

		// Given: an origin that would take ten seconds, and a request to it that is under way
		await using var origin = await TestOrigin.StartAsync(c => Task.Delay(TimeSpan.FromSeconds(10), c.RequestAborted));
		await using var proxy = await TestProxy.StartAsync();
		var request = proxy.Client.GetAsync(origin.UrlFor("/very-slow"));
		await WaitUntilAsync(() => proxy.Activity.Exchanges == 1);

		// When: the proxy is asked to stop with a 300 ms drain period
		var timer = Stopwatch.StartNew();
		var report = await proxy.StopAsync(TimeSpan.FromMilliseconds(300));
		timer.Stop();

		// Then: the stop returned soon after the drain period, reporting the one aborted request; the client's request failed;
		// and the aborted exchange was still logged, with a reason
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
		report.Exchanges.ShouldBe(1);
		report.Drained.ShouldBeFalse();
		await Should.ThrowAsync<HttpRequestException>(() => request);
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Error.ShouldNotBeNull();
		record.Status.ShouldBeNull();   // nothing had been sent to the client, so there is no status to report
	}

	[Fact]
	public async Task Stop_SkipSignalled_StopsAtOnceWithoutWaitingForTheDrainPeriod() {

		// Given: an origin that would take ten seconds, a request to it under way, and a skip signal (the second Ctrl+C)
		await using var origin = await TestOrigin.StartAsync(c => Task.Delay(TimeSpan.FromSeconds(10), c.RequestAborted));
		await using var proxy = await TestProxy.StartAsync();
		var request = proxy.Client.GetAsync(origin.UrlFor("/very-slow"));
		await WaitUntilAsync(() => proxy.Activity.Exchanges == 1);
		using var skip = new CancellationTokenSource();

		// When: the proxy is asked to stop with a 30 second drain period, and the skip signal is given after 200 ms
		var timer = Stopwatch.StartNew();
		var stop = proxy.StopAsync(TimeSpan.FromSeconds(30), skip.Token);
		await Task.Delay(200);
		skip.Cancel();
		var report = await stop;
		timer.Stop();

		// Then: it stopped within moments, not after 30 seconds, and reported the aborted request
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
		report.Exchanges.ShouldBe(1);
		await Should.ThrowAsync<HttpRequestException>(() => request);
	}

	[Fact]
	public async Task Stop_IdleTunnel_EndsAfterABriefQuietPeriodInsteadOfTheWholeDrain() {

		// Given: a tunnel to an echoing TCP origin that is open but silent
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);
		await client.SendAsync($"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n");
		await client.ReadUntilAsync("\r\n\r\n");
		await WaitUntilAsync(() => proxy.Activity.Tunnels == 1);

		// When: the proxy is asked to stop with a 10 second drain period
		var timer = Stopwatch.StartNew();
		var report = await proxy.StopAsync(TimeSpan.FromSeconds(10));
		timer.Stop();

		// Then: the quiet tunnel was closed after about a second, not after ten, nothing needed aborting, and the client sees the connection end
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
		report.Drained.ShouldBeTrue();
		(await client.ReadToEndAsync(timeoutSeconds: 5)).ShouldBeEmpty();
	}

	[Fact]
	public async Task Stop_TunnelThatIsStillMovingData_IsAllowedToFinish() {

		// Given: a tunnel to an origin that sends the characters 1 to 5, one every 300 ms, and then closes
		await using var origin = new TestTcpOrigin(async stream => {
			for (char c = '1'; c <= '5'; c++) {
				await stream.WriteAsync(new[] { (byte)c });
				await Task.Delay(300);
			}
		});
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);
		await client.SendAsync($"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n");
		await client.ReadUntilAsync("\r\n\r\n");
		var first = Encoding.ASCII.GetString(await client.ReadExactlyAsync(1));

		// When: the proxy is asked to stop (with a 10 second drain period) while the data is still arriving
		var stop = proxy.StopAsync(TimeSpan.FromSeconds(10));
		var rest = Encoding.ASCII.GetString(await client.ReadToEndAsync());
		var report = await stop;

		// Then: the client received all five characters, so the transfer was not cut off, and nothing was aborted
		(first + rest).ShouldBe("12345");
		report.Drained.ShouldBeTrue();
	}

	[Fact]
	public async Task Stop_OpenWebSocket_ClientIsToldTheEndpointIsGoingAway() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, an intercepting proxy, and a connected client
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(EchoWebSocket, https: true);
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions {
			Port = 0, Intercept = true, CaDirectory = caFolder.Path, OriginCertificateValidation = (_, _, _, _) => true,
		});
		using var socket = proxy.CreateWebSocket();
		await socket.ConnectAsync(new Uri(origin.UrlFor("/").Replace("https://", "wss://")), CancellationToken.None);
		await WaitUntilAsync(() => proxy.Activity.WebSockets == 1);

		// When: the proxy is asked to stop with a 5 second drain period, and the client answers the close it receives
		var timer = Stopwatch.StartNew();
		var stop = proxy.StopAsync(TimeSpan.FromSeconds(5));
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var received = await socket.ReceiveAsync(new byte[16], cts.Token);
		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ok", cts.Token);
		var report = await stop;
		timer.Stop();

		// Then: the client was sent a proper close with code 1001 (going away), the close handshake finished well inside the drain period,
		// and nothing had to be aborted
		received.MessageType.ShouldBe(WebSocketMessageType.Close);
		socket.CloseStatus.ShouldBe(WebSocketCloseStatus.EndpointUnavailable);
		socket.CloseStatusDescription!.ShouldContain("shutting down");
		report.Drained.ShouldBeTrue();
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task Stop_ConnectionThatNeverSentAnything_DoesNotHoldUpTheStop() {

		// Given: a connection that was opened to the proxy but has not sent a single byte (browsers open spare ones in advance)
		await using var proxy = await TestProxy.StartAsync();
		using var spare = new TcpClient();
		await spare.ConnectAsync("127.0.0.1", proxy.Port);
		await Task.Delay(100);

		// When: the proxy is asked to stop with a 10 second drain period
		var timer = Stopwatch.StartNew();
		var report = await proxy.StopAsync(TimeSpan.FromSeconds(10));
		timer.Stop();

		// Then: the stop did not wait for it
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4));
		report.Drained.ShouldBeTrue();
	}

	[Fact]
	public async Task Stop_IdleHttp2ConnectionThroughInterception_DoesNotHoldUpTheStop() {

		// Given: an intercepting proxy and a client that finished a request over HTTP/2 but keeps its connection open
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions {
			Port = 0, Intercept = true, CaDirectory = caFolder.Path, OriginCertificateValidation = (_, _, _, _) => true,
		});
		(await proxy.Http2Client.GetStringAsync(origin.UrlFor("/"))).ShouldBe("hello");

		// When: the proxy is asked to stop with a 10 second drain period
		var timer = Stopwatch.StartNew();
		var report = await proxy.StopAsync(TimeSpan.FromSeconds(10));
		timer.Stop();

		// Then: the idle connection was closed rather than waited for
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
		report.Drained.ShouldBeTrue();
	}

	[Fact]
	public async Task Stop_CalledTwice_GivesTheSameResultAndDisposeAfterwardsIsFine() {

		// Given: a running proxy
		await using var proxy = await TestProxy.StartAsync();

		// When: it is stopped twice (the second time with a different drain period), and then disposed by the test's cleanup
		var first = proxy.StopAsync(TimeSpan.FromSeconds(1));
		var second = proxy.StopAsync(TimeSpan.FromSeconds(30));
		await first;

		// Then: both calls are the same stop, so the second does not start another one
		second.ShouldBeSameAs(first);
	}
}

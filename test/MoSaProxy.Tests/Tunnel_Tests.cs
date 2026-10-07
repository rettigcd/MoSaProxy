using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class Tunnel_Tests {

	const string Established = "HTTP/1.1 200 Connection Established\r\n\r\n";

	static string ConnectRequest(int port) => $"CONNECT 127.0.0.1:{port} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n";

	[Fact]
	public async Task Connect_ToReachableOrigin_RelaysBytesInBothDirections() {

		// Given: an origin that echoes back whatever it receives, and a running proxy
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client asks for a tunnel to the origin, waits for the go-ahead, then sends "ping"
		await client.SendAsync(ConnectRequest(origin.Port));
		var answer = await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync("ping");
		var echoed = await client.ReadExactlyAsync(4);

		// Then: the proxy answered "200 Connection Established" and the client got "ping" back through the tunnel
		answer.ShouldBe(Established);
		Encoding.ASCII.GetString(echoed).ShouldBe("ping");
	}

	[Fact]
	public async Task Connect_ClientSendsDataInTheSameWriteAsTheRequest_DataIsNotLost() {

		// Given: an echoing origin and a running proxy
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client sends the CONNECT request and the first bytes of the tunneled data in one write
		await client.SendAsync(ConnectRequest(origin.Port) + "early");
		await client.ReadUntilAsync("\r\n\r\n");
		var echoed = await client.ReadExactlyAsync(5);

		// Then: those early bytes reach the origin and come back
		Encoding.ASCII.GetString(echoed).ShouldBe("early");
	}

	[Fact]
	public async Task Connect_RequestArrivesInSmallPieces_StillTunnels() {

		// Given: an echoing origin and a running proxy
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client sends the CONNECT request in three pieces with pauses between them
		var request = ConnectRequest(origin.Port);
		await client.SendAsync(request[..3]);
		await Task.Delay(100);
		await client.SendAsync(request[3..20]);
		await Task.Delay(100);
		await client.SendAsync(request[20..]);
		var answer = await client.ReadUntilAsync("\r\n\r\n");

		// Then: the tunnel is still established
		answer.ShouldBe(Established);
	}

	[Fact]
	public async Task Get_RequestArrivesInSmallPieces_IsStillForwardedAsPlainHttp() {

		// Given: an HTTP origin and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client sends an ordinary proxy GET in two pieces, the first only three bytes long
		await client.SendAsync("GET");
		await Task.Delay(100);
		await client.SendAsync($" {origin.UrlFor("/")} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nConnection: close\r\n\r\n");
		var response = Encoding.Latin1.GetString(await client.ReadToEndAsync());

		// Then: it is not mistaken for a tunnel; the origin's response comes back
		response.ShouldStartWith("HTTP/1.1 200 OK");
		response.ShouldContain("hello");
	}

	[Fact]
	public async Task Connect_ClientFinishesSending_OriginsReplyStillArrives() {

		// Given: an origin that waits for the client to finish sending and then answers "done", and a running proxy
		await using var origin = new TestTcpOrigin(async stream => {
			await stream.CopyToAsync(Stream.Null);
			await stream.WriteAsync("done"u8.ToArray());
		});
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens the tunnel, sends "request", and then signals it will send nothing more
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync("request");
		client.FinishSending();
		var reply = await client.ReadToEndAsync();

		// Then: the origin's late reply "done" still reaches the client
		Encoding.ASCII.GetString(reply).ShouldBe("done");
	}

	[Fact]
	public async Task Connect_OriginClosesTheConnection_ClientSeesTheTunnelClose() {

		// Given: an origin that sends "bye" and immediately closes, and a running proxy
		await using var origin = new TestTcpOrigin(stream => stream.WriteAsync("bye"u8.ToArray()).AsTask());
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens the tunnel and reads until the connection ends
		await client.SendAsync(ConnectRequest(origin.Port));
		var all = Encoding.ASCII.GetString(await client.ReadToEndAsync());

		// Then: it received the go-ahead and "bye", and then the connection closed
		all.ShouldBe(Established + "bye");
	}

	[Fact]
	public async Task Connect_NothingMovesForLongerThanTheIdleTimeout_TunnelIsClosed() {

		// Given: an echoing origin and a proxy that closes tunnels idle for 300 ms
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, TunnelIdleTimeout = TimeSpan.FromMilliseconds(300) });
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens the tunnel and then sends nothing
		await client.SendAsync(ConnectRequest(origin.Port));
		var all = Encoding.ASCII.GetString(await client.ReadToEndAsync(timeoutSeconds: 5));

		// Then: the proxy closes the connection after the go-ahead, without the client having to
		all.ShouldBe(Established);
	}

	[Fact]
	public async Task Connect_OriginRefusesConnection_Returns502() {

		// Given: a running proxy and a loopback port where nothing is listening
		await using var proxy = await TestProxy.StartAsync();
		int deadPort;
		using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) {
			probe.Start();
			deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
		}
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client asks for a tunnel to that port
		await client.SendAsync(ConnectRequest(deadPort));
		var answer = Encoding.ASCII.GetString(await client.ReadToEndAsync());

		// Then: the proxy answers 502 Bad Gateway instead of "200 Connection Established"
		answer.ShouldStartWith("HTTP/1.1 502 Bad Gateway");
	}

	[Theory]
	[InlineData("nonsense")]
	[InlineData("example.com")]
	[InlineData("example.com:abc")]
	[InlineData("example.com:0")]
	[InlineData(":443")]
	public async Task Connect_TargetIsNotHostAndPort_Returns400(string target) {

		// Given: a running proxy
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client sends a CONNECT request whose target is malformed
		await client.SendAsync($"CONNECT {target} HTTP/1.1\r\nHost: x\r\n\r\n");
		var answer = Encoding.ASCII.GetString(await client.ReadToEndAsync());

		// Then: the proxy answers 400 Bad Request
		answer.ShouldStartWith("HTTP/1.1 400 Bad Request");
	}

	[Fact]
	public async Task Connect_CompletedTunnel_IsLoggedWithHostPortAndByteCounts() {

		// Given: an echoing origin and a proxy with an in-memory log
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client tunnels the 7 bytes "payload" to the origin, gets them back, and closes
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync("payload");
		await client.ReadExactlyAsync(7);
		client.Dispose();

		// Then: one tunnel record is logged with CONNECT, the target host and port, status 200 and 7 bytes each way
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Method.ShouldBe("CONNECT");
		record.Mode.ShouldBe("tunnel");
		record.Host.ShouldBe("127.0.0.1");
		record.Port.ShouldBe(origin.Port);
		record.Status.ShouldBe(200);
		record.RequestBytes.ShouldBe(7);
		record.ResponseBytes.ShouldBe(7);
	}

	[Fact]
	public async Task Connect_FailedTunnel_IsLoggedAsRejectedWithAnError() {

		// Given: a running proxy and a loopback port where nothing is listening
		await using var proxy = await TestProxy.StartAsync();
		int deadPort;
		using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) {
			probe.Start();
			deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
		}
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client asks for a tunnel to that port
		await client.SendAsync(ConnectRequest(deadPort));
		await client.ReadToEndAsync();

		// Then: the logged record has status 502, mode "rejected" and an error message
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Status.ShouldBe(502);
		record.Mode.ShouldBe("rejected");
		record.Error.ShouldNotBeNull();
	}

	[Fact]
	public async Task Connect_BrowserStyleRequestHeaders_AreLogged() {

		// Given: an echoing origin and a proxy with an in-memory log
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens a tunnel with a User-Agent header and closes
		await client.SendAsync($"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nUser-Agent: TestBrowser/1.0\r\n\r\n");
		await client.ReadUntilAsync("\r\n\r\n");
		client.Dispose();

		// Then: the log record carries that header
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.RequestHeaders["User-Agent"].ShouldBe("TestBrowser/1.0");
	}
}

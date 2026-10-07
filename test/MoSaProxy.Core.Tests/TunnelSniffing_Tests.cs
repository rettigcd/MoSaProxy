using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>With interception on, a CONNECT tunnel is not always TLS. These tests cover how the proxy tells the cases apart.</summary>
public class TunnelSniffing_Tests {

	static ProxyOptions Intercepting(TempFolder caFolder, TimeSpan? sniffTimeout = null) => new() {
		Port = 0,
		Intercept = true,
		CaDirectory = caFolder.Path,
		SniffTimeout = sniffTimeout ?? TimeSpan.FromSeconds(3),
	};

	static string ConnectRequest(int port) => $"CONNECT 127.0.0.1:{port} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n";

	[Fact]
	public async Task Connect_PlainHttpInsideTheTunnel_IsForwardedAndLoggedAsAnHttpExchange() {

		// Given: a plain-HTTP origin that answers "hello", and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens a tunnel to the origin's port and then sends an ordinary (not encrypted) HTTP request through it
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync($"GET /x HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nConnection: close\r\n\r\n");
		var response = Encoding.Latin1.GetString(await client.ReadToEndAsync());

		// Then: the origin's answer comes back, and the log shows the CONNECT and then a readable GET for the full http:// URL
		response.ShouldStartWith("HTTP/1.1 200 OK");
		response.ShouldContain("hello");
		origin.Seen.Single().PathAndQuery.ShouldBe("/x");
		var records = await proxy.Log.WaitForAsync(2);
		records[0].Method.ShouldBe("CONNECT");
		records[0].Mode.ShouldBe("intercept");
		records[1].Method.ShouldBe("GET");
		records[1].Url.ShouldBe(origin.UrlFor("/x"));
		records[1].Scheme.ShouldBe("http");
		records[1].Mode.ShouldBe("intercept");
	}

	[Fact]
	public async Task Connect_RequestLineArrivesInPieces_IsStillRecognizedAsHttp() {

		// Given: a plain-HTTP origin and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var client = await RawClient.ConnectAsync(proxy.Port);
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");

		// When: the client sends its first request in two pieces, the first being just "GE"
		await client.SendAsync("GE");
		await Task.Delay(150);
		await client.SendAsync($"T /x HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nConnection: close\r\n\r\n");
		var response = Encoding.Latin1.GetString(await client.ReadToEndAsync());

		// Then: it is still treated as HTTP and answered by the origin
		response.ShouldStartWith("HTTP/1.1 200 OK");
		response.ShouldContain("hello");
	}

	[Theory]
	[InlineData("hello there")]
	[InlineData("SSH-2.0-test\r\n")]
	[InlineData("\u0001\u0002\u0003\u0004binary")]
	public async Task Connect_ProtocolThatIsNeitherTlsNorHttp_IsRelayedBlindly(string firstBytes) {

		// Given: a TCP origin that echoes everything back, and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens a tunnel and sends bytes that are not a TLS handshake or an HTTP request
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync(firstBytes);
		var echoed = Encoding.Latin1.GetString(await client.ReadExactlyAsync(firstBytes.Length));
		client.Dispose();

		// Then: the bytes pass through unchanged, and a tunnel record with the byte counts is logged after the CONNECT
		echoed.ShouldBe(firstBytes);
		var records = await proxy.Log.WaitForAsync(2);
		records[0].Mode.ShouldBe("intercept");
		records[1].Mode.ShouldBe("tunnel");
		records[1].RequestBytes.ShouldBe(firstBytes.Length);
		records[1].ResponseBytes.ShouldBe(firstBytes.Length);
	}

	[Fact]
	public async Task Connect_ClientStaysSilentAndTheServerSpeaksFirst_BannerArrivesAfterTheSniffTimeout() {

		// Given: a TCP origin that sends "BANNER" as soon as someone connects (like a mail or SSH server), and an intercepting proxy that waits 300 ms for the client's first bytes
		using var caFolder = new TempFolder();
		await using var origin = new TestTcpOrigin(async stream => {
			await stream.WriteAsync("BANNER"u8.ToArray());
			await stream.CopyToAsync(Stream.Null);
		});
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, TimeSpan.FromMilliseconds(300)));
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: the client opens the tunnel and then sends nothing
		await client.SendAsync(ConnectRequest(origin.Port));
		await client.ReadUntilAsync("\r\n\r\n");
		var banner = Encoding.ASCII.GetString(await client.ReadExactlyAsync(6));

		// Then: after the short wait the proxy treats it as an opaque tunnel and the server's banner reaches the client
		banner.ShouldBe("BANNER");
	}
}

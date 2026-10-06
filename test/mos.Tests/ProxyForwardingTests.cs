using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace mos.Tests;

public class ProxyForwardingTests {

	[Fact]
	public async Task Get_ThroughProxy_ReturnsOriginResponse() {

		// Given: an origin that answers any request with 200 and the text "hello", and a running proxy
		await using var origin = await TestOrigin.StartAsync(async c => { c.Response.Headers["X-Origin"] = "yes"; await c.Response.WriteAsync("hello"); });
		await using var proxy = await TestProxy.StartAsync();

		// When: a client requests the origin's URL through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/page?a=1"));

		// Then: the client gets the origin's status, header and body, and the origin saw the same path and query
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Headers.GetValues("X-Origin").ShouldBe(new[] { "yes" });
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		origin.Seen.Single().PathAndQuery.ShouldBe("/page?a=1");
	}

	[Fact]
	public async Task Post_WithBody_DeliversBodyAndContentTypeToOrigin() {

		// Given: an origin that accepts requests and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync();

		// When: a client posts a JSON body through the proxy
		var content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json");
		await proxy.Client.PostAsync(origin.UrlFor("/submit"), content);

		// Then: the origin received a POST with exactly that body and content type
		var seen = origin.Seen.Single();
		seen.Method.ShouldBe("POST");
		Encoding.UTF8.GetString(seen.Body).ShouldBe("{\"a\":1}");
		seen.Headers.ContentType.ToString().ShouldStartWith("application/json");
	}

	[Fact]
	public async Task Post_ChunkedBody_DeliversWholeBodyToOrigin() {

		// Given: an origin that accepts requests, a running proxy, and a body of unknown length so the client sends it chunked
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync();
		var content = new StreamContent(new NonSeekableStream(new MemoryStream(new byte[100_000])));

		// When: the client posts that body through the proxy
		var response = await proxy.Client.PostAsync(origin.UrlFor("/upload"), content);

		// Then: the request succeeds and the origin received all 100,000 bytes
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		origin.Seen.Single().Body.Length.ShouldBe(100_000);
	}

	[Fact]
	public async Task Get_LargeResponse_ArrivesComplete() {

		// Given: an origin that returns 5 MB of data and a running proxy
		var data = new byte[5 * 1024 * 1024];
		new Random(1).NextBytes(data);
		await using var origin = await TestOrigin.StartAsync(c => c.Response.Body.WriteAsync(data).AsTask());
		await using var proxy = await TestProxy.StartAsync();

		// When: a client downloads it through the proxy
		var received = await proxy.Client.GetByteArrayAsync(origin.UrlFor("/big"));

		// Then: the client receives exactly the same bytes
		received.ShouldBe(data);
	}

	[Fact]
	public async Task Get_OriginRedirects_RedirectIsPassedThroughUnfollowed() {

		// Given: an origin that answers with a 302 redirect, and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => { c.Response.StatusCode = 302; c.Response.Headers.Location = "/elsewhere"; return Task.CompletedTask; });
		await using var proxy = await TestProxy.StartAsync();

		// When: a client requests the URL through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/old"));

		// Then: the client sees the 302 and its Location header, and the origin was asked only once
		response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().ShouldBe("/elsewhere");
		origin.Seen.Count.ShouldBe(1);
	}

	[Fact]
	public async Task Get_ClientSendsHopByHopHeaders_OriginDoesNotSeeThem() {

		// Given: an origin that records headers and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync();

		// When: a client sends a request carrying Proxy-Authorization, Keep-Alive and an X-Custom header
		var request = new HttpRequestMessage(HttpMethod.Get, origin.UrlFor("/"));
		request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic abc");
		request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
		request.Headers.TryAddWithoutValidation("X-Custom", "kept");
		await proxy.Client.SendAsync(request);

		// Then: the origin sees X-Custom but neither of the hop-by-hop headers
		var headers = origin.Seen.Single().Headers;
		headers.ContainsKey("X-Custom").ShouldBeTrue();
		headers.ContainsKey("Proxy-Authorization").ShouldBeFalse();
		headers.ContainsKey("Keep-Alive").ShouldBeFalse();
	}

	[Fact]
	public async Task Get_TwoSequentialRequests_ReuseTheOriginConnection() {

		// Given: an origin that records which connection served each request, and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync();

		// When: a client makes two requests, one after the other, through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/one"));
		await proxy.Client.GetAsync(origin.UrlFor("/two"));

		// Then: both requests reached the origin over the same kept-alive connection
		origin.Seen.Select(r => r.ConnectionId).Distinct().Count().ShouldBe(1);
	}

	[Fact]
	public async Task Get_OriginIsUnreachable_Returns502() {

		// Given: a running proxy and a URL on a loopback port where nothing is listening
		await using var proxy = await TestProxy.StartAsync();
		int deadPort;
		using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) {
			probe.Start();
			deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
		}

		// When: a client requests that URL through the proxy
		var response = await proxy.Client.GetAsync($"http://127.0.0.1:{deadPort}/");

		// Then: the client gets 502 Bad Gateway
		response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
	}

	[Fact]
	public async Task Get_OriginTooSlowToRespond_Returns504() {

		// Given: an origin that waits 5 seconds before responding and a proxy with a 300 ms upstream timeout
		await using var origin = await TestOrigin.StartAsync(async c => await Task.Delay(TimeSpan.FromSeconds(5), c.RequestAborted));
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, UpstreamTimeout = TimeSpan.FromMilliseconds(300) });

		// When: a client requests the slow URL through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/slow"));

		// Then: the client gets 504 Gateway Timeout
		response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
	}

	[Fact]
	public async Task Get_OriginFormTarget_IsRejectedWith400() {

		// Given: a running proxy
		await using var proxy = await TestProxy.StartAsync();

		// When: a client sends a normal web-server style request ("GET /path" with no full URL), as if talking to a site directly
		var answer = await RawHttp.SendAsync(proxy.Port, "GET /path HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\n\r\n");

		// Then: the proxy answers 400 Bad Request because it cannot tell where to forward the request
		answer.ShouldStartWith("HTTP/1.1 400");
	}

	[Fact]
	public async Task Start_AnyOptions_ListensOnLoopbackOnly() {

		// Given: a running proxy
		await using var proxy = await TestProxy.StartAsync();

		// When: we look up which local addresses accept connections on the proxy's port
		var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
			.Where(l => l.Port == proxy.Port).Select(l => l.Address).ToList();

		// Then: the only listening address is 127.0.0.1
		listeners.ShouldBe(new[] { IPAddress.Loopback });
	}

	sealed class NonSeekableStream(Stream inner) : Stream {
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}

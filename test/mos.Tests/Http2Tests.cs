using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace mos.Tests;

public class Http2Tests {

	/// <summary>Options for an intercepting proxy that accepts the fake origin's self-signed certificate.</summary>
	static ProxyOptions Intercepting(TempFolder caFolder, string? captureDirectory = null, bool enableHttp2 = true) => new() {
		Port = 0,
		Intercept = true,
		EnableHttp2 = enableHttp2,
		CaDirectory = caFolder.Path,
		CaptureDirectory = captureDirectory,
		OriginCertificateValidation = (_, _, _, _) => true,
	};

	/// <summary>A request body whose length is not known in advance, so HTTP/2 sends it without a Content-Length.</summary>
	sealed class UnknownLengthContent(byte[] data) : HttpContent {
		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) {
			for (int offset = 0; offset < data.Length; offset += 1000)
				await stream.WriteAsync(data.AsMemory(offset, Math.Min(1000, data.Length - offset)));
		}
		protected override bool TryComputeLength(out long length) { length = -1; return false; }
	}

	[Fact]
	public async Task Get_Http2ClientAndHttp2Origin_UsesHttp2OnBothSides() {

		// Given: an HTTPS origin that supports HTTP/2 and answers "hello", and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client that asks for HTTP/2 requests a page through the proxy
		var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/page"));

		// Then: the client was served over HTTP/2, the origin also saw the request as HTTP/2, and the body is intact
		response.Version.ShouldBe(HttpVersion.Version20);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		origin.Seen.Single().Protocol.ShouldBe("HTTP/2");
	}

	[Fact]
	public async Task Get_Http2_LogsTheProtocolOfBothSides() {

		// Given: an HTTP/2-capable HTTPS origin and an intercepting proxy with an in-memory log
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: an HTTP/2 client requests a page through the proxy
		await proxy.Http2Client.GetAsync(origin.UrlFor("/page"));

		// Then: the logged request shows HTTP/2 between the client and the proxy and HTTP/2 between the proxy and the origin
		var records = await proxy.Log.WaitForAsync(2);
		var get = records.Single(r => r.Method == "GET");
		get.ClientHttpVersion.ShouldBe("HTTP/2");
		get.OriginHttpVersion.ShouldBe("HTTP/2");
		get.Scheme.ShouldBe("https");
		get.Status.ShouldBe(200);
	}

	[Fact]
	public async Task Get_OriginOnlySpeaksHttp1_Http2ClientStillWorks() {

		// Given: an HTTPS origin that does not offer HTTP/2, and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true, http1Only: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: an HTTP/2 client requests a page through the proxy
		var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/page"));

		// Then: the client still gets HTTP/2 from the proxy while the proxy fell back to HTTP/1.1 towards the origin, and the log says so
		response.Version.ShouldBe(HttpVersion.Version20);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		origin.Seen.Single().Protocol.ShouldBe("HTTP/1.1");
		var get = (await proxy.Log.WaitForAsync(2)).Single(r => r.Method == "GET");
		get.ClientHttpVersion.ShouldBe("HTTP/2");
		get.OriginHttpVersion.ShouldBe("HTTP/1.1");
	}

	[Fact]
	public async Task Get_Http1ClientAndHttp2Origin_ProxyTranslatesBetweenTheProtocols() {

		// Given: an HTTP/2-capable HTTPS origin and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client that only speaks HTTP/1.1 requests a page through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/page"));

		// Then: the client was served over HTTP/1.1 while the proxy used HTTP/2 towards the origin
		response.Version.ShouldBe(HttpVersion.Version11);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		origin.Seen.Single().Protocol.ShouldBe("HTTP/2");
		var get = (await proxy.Log.WaitForAsync(2)).Single(r => r.Method == "GET");
		get.ClientHttpVersion.ShouldBe("HTTP/1.1");
		get.OriginHttpVersion.ShouldBe("HTTP/2");
	}

	[Fact]
	public async Task Get_Http2TurnedOff_ClientIsServedOverHttp11() {

		// Given: an HTTP/2-capable HTTPS origin and an intercepting proxy started with HTTP/2 switched off
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, enableHttp2: false));

		// When: a client that asks for HTTP/2 requests a page through the proxy
		var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/page"));

		// Then: the connection to the client falls back to HTTP/1.1
		response.Version.ShouldBe(HttpVersion.Version11);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		(await proxy.Log.WaitForAsync(2)).Single(r => r.Method == "GET").ClientHttpVersion.ShouldBe("HTTP/1.1");
	}

	[Fact]
	public async Task Get_PlainHttpOrigin_StaysHttp11EvenForAnHttp2Client() {

		// Given: a plain-HTTP origin and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("plain"));
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client that asks for HTTP/2 requests an http:// page through the proxy (no TLS, so no HTTP/2)
		var text = await proxy.Http2Client.GetStringAsync(origin.UrlFor("/"));

		// Then: it works over HTTP/1.1 on both sides
		text.ShouldBe("plain");
		origin.Seen.Single().Protocol.ShouldBe("HTTP/1.1");
		var get = (await proxy.Log.WaitForAsync()).Single();
		get.ClientHttpVersion.ShouldBe("HTTP/1.1");
		get.OriginHttpVersion.ShouldBe("HTTP/1.1");
	}

	[Fact]
	public async Task Post_Http2BodyWithoutContentLength_ReachesTheOrigin() {

		// Given: an HTTP/2-capable HTTPS origin and an intercepting proxy, and a 50,000-byte body whose length the client does not announce
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		var body = new byte[50_000];
		new Random(3).NextBytes(body);

		// When: the client posts it over HTTP/2 through the proxy
		var response = await proxy.Http2Client.PostAsync(origin.UrlFor("/upload"), new UnknownLengthContent(body));

		// Then: the request succeeds and the origin received every byte (HTTP/2 needs no Content-Length or chunking, so the proxy must not assume a missing one means no body)
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		var seen = origin.Seen.Single();
		seen.Protocol.ShouldBe("HTTP/2");
		seen.Body.ShouldBe(body);
	}

	[Fact]
	public async Task Get_Http2LargeResponse_ArrivesComplete() {

		// Given: an HTTP/2-capable HTTPS origin that returns 5 MB, and an intercepting proxy
		using var caFolder = new TempFolder();
		var data = new byte[5 * 1024 * 1024];
		new Random(5).NextBytes(data);
		await using var origin = await TestOrigin.StartAsync(c => c.Response.Body.WriteAsync(data).AsTask(), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: an HTTP/2 client downloads it through the proxy (this exercises HTTP/2 flow control on both sides)
		var received = await proxy.Http2Client.GetByteArrayAsync(origin.UrlFor("/big"));

		// Then: the client receives exactly the same bytes
		received.ShouldBe(data);
	}

	[Fact]
	public async Task Get_ManyConcurrentHttp2Requests_ShareOneConnectionOnEachSide() {

		// Given: an HTTP/2-capable HTTPS origin that takes 150 ms per request, an intercepting proxy, and a first request that has opened the connections
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => { await Task.Delay(150); await c.Response.WriteAsync("ok"); }, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		await proxy.Http2Client.GetAsync(origin.UrlFor("/warmup"));

		// When: the client sends 30 requests at the same moment
		var timer = Stopwatch.StartNew();
		var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => proxy.Http2Client.GetAsync(origin.UrlFor($"/r{i}"))));
		timer.Stop();

		// Then: all succeed, they overlapped (30 x 150 ms one after another would take 4.5 s), there was one tunnel to the proxy,
		// and the origin saw all 31 requests arrive over a single connection
		responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
		timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
		origin.Seen.Count.ShouldBe(31);
		origin.Seen.Select(r => r.ConnectionId).Distinct().Count().ShouldBe(1);
		var records = await proxy.Log.WaitForAsync(32);
		records.Count(r => r.Method == "CONNECT").ShouldBe(1);
		records.Count(r => r.Method == "GET").ShouldBe(31);
	}

	[Fact]
	public async Task Get_OriginSendsTrailers_ClientReceivesThem() {

		// Given: an HTTP/2-capable HTTPS origin that sends the body "data" followed by a trailer header, and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => {
			c.Response.DeclareTrailer("X-Checksum");
			await c.Response.WriteAsync("data");
			c.Response.AppendTrailer("X-Checksum", "abc123");
		}, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: an HTTP/2 client reads the whole response
		var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/"));
		var body = await response.Content.ReadAsStringAsync();

		// Then: the body arrived and so did the trailer header that followed it
		body.ShouldBe("data");
		response.TrailingHeaders.GetValues("X-Checksum").ShouldBe(new[] { "abc123" });
	}

	[Fact]
	public async Task Get_Http2_CaptureHoldsTheRequestAndResponseAsReadableText() {

		// Given: an HTTP/2-capable HTTPS origin that answers "hello", and an intercepting proxy capturing into a new folder
		using var caFolder = new TempFolder();
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, captureFolder.Path));

		// When: an HTTP/2 client requests a page through the proxy
		await proxy.Http2Client.GetAsync(origin.UrlFor("/page"));

		// Then: exchange 2 (exchange 1 is the CONNECT) is captured as text: the request line says HTTP/2, and the response starts with an HTTP/2 status line and ends with the body
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));
		var request = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "2_c.txt")));
		request.ShouldStartWith("GET /page HTTP/2\r\n");
		request.ShouldEndWith("\r\n\r\n");
		var response = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "2_s.txt")));
		response.ShouldStartWith("HTTP/2 200 OK\r\n");
		response.ShouldEndWith("\r\n\r\nhello");
	}

	[Theory]
	[InlineData(1, 0, "HTTP/1.0")]
	[InlineData(1, 1, "HTTP/1.1")]
	[InlineData(2, 0, "HTTP/2")]
	[InlineData(3, 0, "HTTP/3")]
	public void Format_Version_UsesTheNamesSeenInLogsAndCaptures(int major, int minor, string expected) {

		// Given: a protocol version (supplied by the test data)

		// When: it is formatted
		var text = HttpVersions.Format(new Version(major, minor));

		// Then: HTTP/2 and later have no ".0", while HTTP/1.x keep their minor version
		text.ShouldBe(expected);
	}
}

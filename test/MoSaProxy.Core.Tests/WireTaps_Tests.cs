using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>The capture files hold the exact bytes that crossed the wire (HTTP/1.x), not a rebuild of what Kestrel parsed.</summary>
public class WireTaps_Tests {

	// Deliberately unusual bytes: lower-case and odd header names, extra spaces, a chunk extension, a duplicate header.
	const string FixedResponse = "HTTP/1.1 200 OK\r\ncontent-LENGTH:5\r\nX-Odd:   spaced  \r\n\r\nhello";
	const string ChunkedResponse = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3;ext=1\r\nabc\r\n2\r\nde\r\n0\r\n\r\n";
	const string PostResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok";

	static byte[] B(string text) => Encoding.Latin1.GetBytes(text);

	static byte[] OriginReply(string target) =>
		B(target.EndsWith("/chunked") ? ChunkedResponse : target.EndsWith("/post") ? PostResponse : FixedResponse);

	static ProxyOptions Capturing(TempFolder folder, long? maxBody = null) => new() {
		Port = 0, CaptureDirectory = folder.Path, CaptureMaxBodyBytes = maxBody,
		Intercept = true, CaDirectory = Path.Combine(folder.Path, "ca"), OriginCertificateValidation = (_, _, _, _) => true,
	};

	/// <summary>Reads until <paramref name="count"/> responses have arrived.</summary>
	static async Task ReadResponsesAsync(Stream stream, params string[] methods) {
		var received = new List<byte>();
		var buffer = new byte[4096];
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		int position = 0;
		foreach (var method in methods) {
			while (Http1Framing.FindResponse(received.ToArray(), position, method) is not { Complete: true } found) {
				int n = await stream.ReadAsync(buffer, timeout.Token);
				if (n == 0) return;
				received.AddRange(buffer.AsSpan(0, n).ToArray());
			}
			position += Http1Framing.FindResponse(received.ToArray(), position, method)!.Value.Length;
		}
	}

	/// <summary>Waits until exchange <paramref name="number"/>'s metadata file (written last) exists, then reads one of its files.</summary>
	static async Task<byte[]> CapturedAsync(TestProxy proxy, int number, string suffix) {
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_m.xml"));
		return File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_{suffix}.txt"));
	}

	[Fact]
	public async Task PlainHttp_ThreeRequestsOnOneConnection_EachFileHoldsExactlyThatExchangesBytes() {

		// Given: a raw origin answering with unusual bytes, a capturing proxy, and a raw client with three requests to send on one connection
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(OriginReply);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string host = $"127.0.0.1:{origin.Port}";
		string get = $"GET http://{host}/fixed HTTP/1.1\r\nhost: {host}\r\nX-Odd:    two  spaces\r\nuser-AGENT: raw\r\nX-Dup: 1\r\nX-Dup: 2\r\n\r\n";
		string chunked = $"GET http://{host}/chunked HTTP/1.1\r\nHost: {host}\r\n\r\n";
		string post = $"POST http://{host}/post HTTP/1.1\r\nHost: {host}\r\nContent-Length: 4\r\n\r\nabcd";

		// When: the client sends the three requests, one after another, on the same connection
		using var tcp = new TcpClient();
		await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
		var stream = tcp.GetStream();
		foreach (var request in new[] { get, chunked, post }) await stream.WriteAsync(B(request));
		await ReadResponsesAsync(stream, "GET", "GET", "POST");

		// Then: each exchange's _c file is exactly the request that was sent, and each _s file exactly the response the origin wrote
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(get));
		(await CapturedAsync(proxy, 2, "c")).ShouldBe(B(chunked));
		(await CapturedAsync(proxy, 3, "c")).ShouldBe(B(post));
		(await CapturedAsync(proxy, 1, "s")).ShouldBe(B(FixedResponse));
		(await CapturedAsync(proxy, 2, "s")).ShouldBe(B(ChunkedResponse));
		(await CapturedAsync(proxy, 3, "s")).ShouldBe(B(PostResponse));
	}

	[Fact]
	public async Task InterceptedHttps_TwoRequests_FilesHoldTheDecryptedBytesExactly() {

		// Given: a raw TLS origin, an intercepting and capturing proxy, and a client that has opened a CONNECT tunnel and finished TLS with the proxy
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(OriginReply, tls: true);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string host = $"127.0.0.1:{origin.Port}";
		using var tcp = new TcpClient();
		await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
		var network = tcp.GetStream();
		await network.WriteAsync(B($"CONNECT {host} HTTP/1.1\r\nHost: {host}\r\n\r\n"));
		var reply = new byte[1024];
		Encoding.Latin1.GetString(reply, 0, await network.ReadAsync(reply)).ShouldStartWith("HTTP/1.1 200");
		await using var tls = new SslStream(network, false, (_, _, _, _) => true);
		await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "127.0.0.1", ApplicationProtocols = [SslApplicationProtocol.Http11] });
		string get = $"GET /fixed HTTP/1.1\r\nhost: {host}\r\nX-Odd:    two  spaces\r\n\r\n";
		string post = $"POST /post HTTP/1.1\r\nHost: {host}\r\nContent-Length: 4\r\n\r\nabcd";

		// When: two requests are sent through the tunnel
		await tls.WriteAsync(B(get));
		await tls.WriteAsync(B(post));
		await ReadResponsesAsync(tls, "GET", "POST");

		// Then: exchanges 2 and 3 (exchange 1 is the CONNECT) hold exactly what was written inside TLS, request and response
		(await CapturedAsync(proxy, 2, "c")).ShouldBe(B(get));
		(await CapturedAsync(proxy, 3, "c")).ShouldBe(B(post));
		(await CapturedAsync(proxy, 2, "s")).ShouldBe(B(FixedResponse));
		(await CapturedAsync(proxy, 3, "s")).ShouldBe(B(PostResponse));
	}

	[Fact]
	public async Task PlainHttp_ClientSendsAChunkedRequestBody_FileHoldsTheChunkedBytes() {

		// Given: a raw origin, a capturing proxy, and a request whose body is sent chunked
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(OriginReply);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string host = $"127.0.0.1:{origin.Port}";
		string request = $"POST http://{host}/post HTTP/1.1\r\nHost: {host}\r\nTransfer-Encoding: chunked\r\n\r\n4;a=b\r\nabcd\r\n0\r\n\r\n";

		// When: it goes through the proxy
		using var tcp = new TcpClient();
		await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
		var stream = tcp.GetStream();
		await stream.WriteAsync(B(request));
		await ReadResponsesAsync(stream, "POST");

		// Then: the _c file has the chunk framing (including the extension) as the client wrote it
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(request));
	}

	[Fact]
	public async Task PlainHttp_BodyLimit_KeepsTheWholeHeadAndCutsTheBodyAndSaysSo() {

		// Given: a capture limit of 3 body bytes, and a request with a 10-byte body
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(OriginReply);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder, maxBody: 3));
		string host = $"127.0.0.1:{origin.Port}";
		string head = $"POST http://{host}/post HTTP/1.1\r\nHost: {host}\r\nContent-Length: 10\r\n\r\n";

		// When: it goes through the proxy
		using var tcp = new TcpClient();
		await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
		var stream = tcp.GetStream();
		await stream.WriteAsync(B(head + "0123456789"));
		await ReadResponsesAsync(stream, "POST");

		// Then: the _c file is the head plus the first 3 body bytes, and the metadata says the request body was truncated
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(head + "012"));
		XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml")).Root!.Element("RequestBodyTruncated")!.Value.ShouldBe("true");
	}

	[Fact]
	public async Task Http2Client_FilesAreStillWrittenAsHttp1StyleText() {

		// Given: an HTTPS origin that speaks HTTP/2 and an intercepting, capturing proxy
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hi"), https: true);
		await using var proxy = await TestProxy.StartAsync(new ProxyOptions {
			Port = 0, CaptureDirectory = folder.Path, Intercept = true, CaDirectory = Path.Combine(folder.Path, "ca"), OriginCertificateValidation = (_, _, _, _) => true,
		});
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: a client asks for a page over HTTP/2
		using var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/h2"));
		response.Version.ShouldBe(HttpVersion.Version20);

		// Then: the request and response files exist as readable text (not the binary HTTP/2 frames)
		string request = Encoding.Latin1.GetString(await CapturedAsync(proxy, 2, "c"));
		request.ShouldContain("GET ");
		request.ShouldNotContain("PRI * HTTP/2.0");
		Encoding.Latin1.GetString(await CapturedAsync(proxy, 2, "s")).ShouldStartWith("HTTP/2 200");
	}
}

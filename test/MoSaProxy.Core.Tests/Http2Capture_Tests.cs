using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>HTTP/2 exchanges are captured from the frames: headers in the order sent, written as HTTP/1.1-style text.</summary>
public class Http2Capture_Tests {

	static ProxyOptions Capturing(TempFolder folder) => new() {
		Port = 0, CaptureDirectory = folder.Path,
		Intercept = true, CaDirectory = Path.Combine(folder.Path, "ca"), OriginCertificateValidation = (_, _, _, _) => true,
	};

	static async Task<string> TextAsync(TestProxy proxy, int number, string suffix) {
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_m.xml"));
		await Task.Delay(50);
		return Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_{suffix}.txt")));
	}

	[Fact]
	public async Task Get_OriginSendsHeadersInAnOrder_ResponseFileKeepsThatOrderAndBody() {

		// Given: an HTTP/2 origin that sets two headers in a known order and a body, behind an intercepting, capturing proxy
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => {
			c.Response.Headers["X-Second"] = "2";
			c.Response.Headers["X-First"] = "1";
			await c.Response.WriteAsync("hello");
		}, https: true);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: a client fetches it over HTTP/2
		using var response = await proxy.Http2Client.GetAsync(origin.UrlFor("/page?a=1"));
		response.Version.ShouldBe(System.Net.HttpVersion.Version20);

		// Then: the request file starts with the request line and a Host header, and the response file has the status line,
		// the two headers in the order the origin set them, and the body (exchange 2; exchange 1 is the CONNECT)
		string request = await TextAsync(proxy, 2, "c");
		request.ShouldStartWith($"GET /page?a=1 HTTP/2\r\nHost: 127.0.0.1:{origin.Port}\r\n");
		request.ShouldEndWith("\r\n\r\n");
		string captured = await TextAsync(proxy, 2, "s");
		captured.ShouldStartWith("HTTP/2 200 OK\r\n");
		captured.IndexOf("x-second: 2\r\n", StringComparison.Ordinal).ShouldBeGreaterThan(0);
		captured.IndexOf("x-second: 2\r\n", StringComparison.Ordinal).ShouldBeLessThan(captured.IndexOf("x-first: 1\r\n", StringComparison.Ordinal));
		captured.ShouldEndWith("\r\n\r\nhello");
	}

	[Fact]
	public async Task Post_ClientSendsABody_RequestFileHoldsTheBody() {

		// Given: an HTTP/2 origin and an intercepting, capturing proxy
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask, https: true);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: a client posts a 3,000-byte body over HTTP/2
		var body = new string('x', 3000);
		using var response = await proxy.Http2Client.PostAsync(origin.UrlFor("/up"), new StringContent(body, Encoding.ASCII, "text/plain"));

		// Then: the request file has the POST line and ends with exactly that body
		string request = await TextAsync(proxy, 2, "c");
		request.ShouldStartWith("POST /up HTTP/2\r\n");
		request.ShouldEndWith("\r\n\r\n" + body);
	}

	[Fact]
	public async Task Get_ManyRequestsAtOnce_EachExchangeGetsItsOwnResponse() {

		// Given: an HTTP/2 origin that answers each path with its own text, slower for the earlier ones so responses finish out of order
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => {
			int n = int.Parse(c.Request.Path.Value!["/p".Length..]);
			await Task.Delay((5 - n) * 40);
			await c.Response.WriteAsync("body of " + c.Request.Path);
		}, https: true);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: five requests are sent at the same moment over one HTTP/2 connection
		await Task.WhenAll(Enumerable.Range(0, 5).Select(n => proxy.Http2Client.GetStringAsync(origin.UrlFor($"/p{n}"))));

		// Then: in every exchange, the request file and the response file belong to the same path as the exchange's own metadata
		await proxy.Log.WaitForAsync(6);   // the CONNECT and five requests
		int checkedExchanges = 0;
		for (int number = 2; number <= 6; number++) {
			string request = await TextAsync(proxy, number, "c");
			string response = await TextAsync(proxy, number, "s");
			string path = new Uri(XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_m.xml")).Root!.Element("Url")!.Value).AbsolutePath;
			request.ShouldStartWith($"GET {path} HTTP/2\r\n");
			response.ShouldEndWith("body of " + path);
			checkedExchanges++;
		}
		checkedExchanges.ShouldBe(5);
	}
}

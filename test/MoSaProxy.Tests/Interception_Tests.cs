using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class Interception_Tests {

	/// <summary>Options for an intercepting proxy that accepts the fake origin's self-signed certificate.</summary>
	static ProxyOptions Intercepting(TempFolder caFolder, string? captureDirectory = null, params string[] bypass) => new() {
		Port = 0,
		Intercept = true,
		InterceptBypass = bypass,
		CaDirectory = caFolder.Path,
		CaptureDirectory = captureDirectory,
		OriginCertificateValidation = (_, _, _, _) => true,
	};

	[Fact]
	public async Task Get_HttpsOrigin_ThroughInterceptingProxy_ReturnsTheOriginsResponse() {

		// Given: an HTTPS origin that answers "hello", and a proxy that decrypts HTTPS using its own root certificate
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client that trusts the MoSaProxy root requests a page over HTTPS through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/page?a=1"));

		// Then: the client gets the origin's status and body, and the origin saw a normal request for that path and query
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		var seen = origin.Seen.Single();
		seen.Method.ShouldBe("GET");
		seen.PathAndQuery.ShouldBe("/page?a=1");
		seen.Headers.Host.ToString().ShouldBe($"127.0.0.1:{origin.Port}");
	}

	[Fact]
	public async Task Get_Intercepted_ClientIsShownACertificateIssuedByTheMoSaProxyRoot() {

		// Given: an HTTPS origin with its own self-signed certificate, and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client requests a page over HTTPS through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/"));

		// Then: the certificate the client saw was issued by the MoSaProxy root, not the origin's own certificate
		var seen = proxy.ServerCertificateSeenByClient!;
		seen.Issuer.ShouldBe(proxy.Authority!.Root.Subject);
		seen.Thumbprint.ShouldNotBe(origin.Certificate!.Thumbprint);
	}

	[Fact]
	public async Task Get_Intercepted_IsLoggedWithTheFullHttpsUrlAndHeaders() {

		// Given: an HTTPS origin and an intercepting proxy with an in-memory log
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client requests a page over HTTPS with a custom header
		var request = new HttpRequestMessage(HttpMethod.Get, origin.UrlFor("/page?a=1"));
		request.Headers.TryAddWithoutValidation("X-Test", "visible");
		await proxy.Client.SendAsync(request);

		// Then: the log has the CONNECT, then the decrypted request with its full https URL, status, size and the custom header
		var records = await proxy.Log.WaitForAsync(2);
		records.Count.ShouldBe(2);
		records[0].Method.ShouldBe("CONNECT");
		records[0].Mode.ShouldBe("intercept");
		records[0].Status.ShouldBe(200);
		var get = records[1];
		get.Method.ShouldBe("GET");
		get.Url.ShouldBe(origin.UrlFor("/page?a=1"));
		get.Scheme.ShouldBe("https");
		get.Mode.ShouldBe("intercept");
		get.Status.ShouldBe(200);
		get.ResponseBytes.ShouldBe(5);
		get.RequestHeaders["X-Test"].ShouldBe("visible");
	}

	[Fact]
	public async Task Post_Intercepted_BodyReachesTheOriginAndCaptureHoldsThePlaintext() {

		// Given: an HTTPS origin that answers "hello", and an intercepting proxy capturing into a new folder
		using var caFolder = new TempFolder();
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, captureFolder.Path));

		// When: a client posts "name=value" over HTTPS through the proxy
		await proxy.Client.PostAsync(origin.UrlFor("/form"), new StringContent("name=value", Encoding.UTF8, "application/x-www-form-urlencoded"));

		// Then: the origin received the body, and the capture holds the readable (decrypted) request and response as exchange 2 (exchange 1 is the CONNECT)
		origin.Seen.Single().Body.ShouldBe(Encoding.UTF8.GetBytes("name=value"));
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));
		Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "1_c.txt"))).ShouldStartWith("CONNECT 127.0.0.1:");
		var request = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "2_c.txt")));
		request.ShouldStartWith("POST /form HTTP/1.1\r\n");
		request.ShouldEndWith("\r\n\r\nname=value");
		Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "2_s.txt"))).ShouldEndWith("\r\n\r\nhello");
		XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml")).Root!.Element("Url")!.Value.ShouldBe(origin.UrlFor("/form"));
	}

	[Fact]
	public async Task Get_TwoRequestsOnTheSameTunnel_AreBothForwardedAndLogged() {

		// Given: an HTTPS origin and an intercepting proxy with an in-memory log
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client makes two requests, one after the other (the client reuses its connection)
		await proxy.Client.GetAsync(origin.UrlFor("/one"));
		await proxy.Client.GetAsync(origin.UrlFor("/two"));

		// Then: the origin saw both paths, and the log has a single CONNECT followed by two decrypted GETs
		origin.Seen.Select(r => r.PathAndQuery).ShouldBe(new[] { "/one", "/two" });
		var records = await proxy.Log.WaitForAsync(3);
		records.Select(r => r.Method).ShouldBe(new[] { "CONNECT", "GET", "GET" });
	}

	[Fact]
	public async Task Get_HostInTheBypassList_StaysABlindTunnel() {

		// Given: an HTTPS origin, and an intercepting proxy told never to intercept 127.0.0.1
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, null, "127.0.0.1"));
		proxy.AlsoTrust.Add(origin.Certificate!.Thumbprint);

		// When: a client requests a page from that host over HTTPS through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/"));

		// Then: the client talks to the origin directly through the tunnel: it sees the origin's own certificate, and only a tunnel record is logged
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
		proxy.ServerCertificateSeenByClient!.Thumbprint.ShouldBe(origin.Certificate!.Thumbprint);
		proxy.Client.Dispose(); // a blind tunnel is logged when it closes
		var records = await proxy.Log.WaitForAsync();
		records.Single().Mode.ShouldBe("tunnel");
	}

	[Theory]
	[InlineData("example.com", "example.com", true)]
	[InlineData("EXAMPLE.com", "example.COM", true)]
	[InlineData("example.com", "www.example.com", false)]
	[InlineData("*.example.com", "www.example.com", true)]
	[InlineData("*.example.com", "a.b.example.com", true)]
	[InlineData("*.example.com", "example.com", false)]
	[InlineData("*.example.com", "badexample.com", false)]
	public void Matches_Pattern_AgainstHost(string pattern, string host, bool expected) {

		// Given: a bypass pattern and a host name (supplied by the test data)

		// When: they are compared
		var matches = TunnelHandler.Matches(pattern, host);

		// Then: they match only when the host equals the pattern, or is a subdomain for a "*." pattern
		matches.ShouldBe(expected);
	}

	[Fact]
	public async Task Get_OriginCertificateCannotBeTrusted_ClientGets502() {

		// Given: an HTTPS origin with a self-signed certificate, and an intercepting proxy that validates origin certificates normally
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		var options = new ProxyOptions { Port = 0, Intercept = true, CaDirectory = caFolder.Path };
		await using var proxy = await TestProxy.StartAsync(options);

		// When: a client requests a page from that origin over HTTPS
		var response = await proxy.Client.GetAsync(origin.UrlFor("/"));

		// Then: the proxy refuses to relay a response from an untrusted origin: the client gets 502, and the origin never saw the request
		response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
		origin.Seen.ShouldBeEmpty();
		var records = await proxy.Log.WaitForAsync(2);
		records[1].Status.ShouldBe(502);
		records[1].Error.ShouldNotBeNull();
	}

	[Fact]
	public async Task Get_ClientDoesNotTrustTheMoSaProxyRoot_HandshakeFailureIsLoggedAndTheProxyKeepsWorking() {

		// Given: an HTTP origin and an HTTPS origin, and an intercepting proxy used by a client that does not trust the MoSaProxy root
		using var caFolder = new TempFolder();
		await using var httpsOrigin = await TestOrigin.StartAsync(c => Task.CompletedTask, https: true);
		await using var httpOrigin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("plain"));
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder), clientTrustsProxyCa: false);

		// When: the client tries an HTTPS page (it rejects the proxy's certificate) and then a plain HTTP page
		await Should.ThrowAsync<HttpRequestException>(() => proxy.Client.GetAsync(httpsOrigin.UrlFor("/")));
		var plain = await proxy.Client.GetStringAsync(httpOrigin.UrlFor("/"));

		// Then: the failed handshake is logged with a hint about the root certificate, and the plain request still works
		plain.ShouldBe("plain");
		var records = await proxy.Log.WaitForAsync(3);
		records.ShouldContain(r => r.Error != null && r.Error.Contains("TLS handshake"));
		httpsOrigin.Seen.ShouldBeEmpty();
	}

	[Fact]
	public async Task Get_Intercepted_LeavesNoCertificatesInTheWindowsCertificateStores() {

		// Given: an HTTPS origin and an intercepting proxy; the test only runs on Windows, where the certificate stores live
		if (!OperatingSystem.IsWindows()) return;
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"), https: true);
		string rootThumbprint;
		await using (var proxy = await TestProxy.StartAsync(Intercepting(caFolder))) {

			// When: a client makes a request over HTTPS through the proxy (so the TLS stack uses the generated certificates), and the proxy is then shut down
			await proxy.Client.GetAsync(origin.UrlFor("/"));
			rootThumbprint = proxy.Authority!.Root.Thumbprint;
		}

		// Then: none of the certificates it made were left behind in the current user's certificate stores
		// (an earlier design made .NET install them into the "Intermediate Certification Authorities" store, where stale copies broke later chain building)
		foreach (var storeName in new[] { System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority, System.Security.Cryptography.X509Certificates.StoreName.Root, System.Security.Cryptography.X509Certificates.StoreName.My }) {
			using var store = new System.Security.Cryptography.X509Certificates.X509Store(storeName, System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
			store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
			store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, rootThumbprint, false).Count.ShouldBe(0);
			store.Certificates.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>()
				.Count(c => c.Issuer.Contains("mos Proxy Root CA") && c.Thumbprint == rootThumbprint).ShouldBe(0);
		}
	}

	[Fact]
	public async Task Get_AbsoluteHttpsTargetWithoutATunnel_IsStillRejected() {

		// Given: an intercepting proxy
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));

		// When: a client sends a plain-text proxy request whose target is an https:// URL (instead of using CONNECT)
		var answer = await RawHttp.SendAsync(proxy.Port, "GET https://example.com/ HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\n\r\n");

		// Then: it is rejected with 400, because HTTPS must go through CONNECT
		answer.ShouldStartWith("HTTP/1.1 400");
	}
}

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mos.Tests;

/// <summary>What the fake origin saw for one request.</summary>
public sealed record SeenRequest(string Method, string PathAndQuery, IHeaderDictionary Headers, byte[] Body, string ConnectionId, string Protocol);

/// <summary>A small in-process HTTP server on loopback that stands in for a real web site.</summary>
public sealed class TestOrigin : IAsyncDisposable {
	readonly WebApplication _app;
	readonly List<SeenRequest> _seen = new();

	readonly string _scheme;

	TestOrigin(WebApplication app, int port, string scheme, X509Certificate2? certificate) { _app = app; Port = port; _scheme = scheme; Certificate = certificate; }

	public int Port { get; }

	/// <summary>The certificate this origin presents when it serves HTTPS (self-signed, so a client must opt in to trust it).</summary>
	public X509Certificate2? Certificate { get; }
	public IReadOnlyList<SeenRequest> Seen { get { lock (_seen) return _seen.ToArray(); } }

	/// <param name="https">Serve HTTPS with a self-signed certificate (which also allows HTTP/2).</param>
	/// <param name="http1Only">Do not offer HTTP/2, so a client has to fall back to HTTP/1.1.</param>
	public static async Task<TestOrigin> StartAsync(Func<HttpContext, Task> respond, bool https = false, bool http1Only = false) {
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		var certificate = https ? CreateSelfSignedCertificate() : null;
		builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, listen => {
			if (certificate is not null) listen.UseHttps(certificate);
			if (http1Only) listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
		}));
		var app = builder.Build();
		TestOrigin? origin = null;
		app.Run(async context => {
			using var buffer = new MemoryStream();
			await context.Request.Body.CopyToAsync(buffer);
			var seen = new SeenRequest(context.Request.Method, context.Request.Path + context.Request.QueryString,
				new HeaderDictionary(context.Request.Headers.ToDictionary(h => h.Key, h => h.Value)), buffer.ToArray(), context.Connection.Id, context.Request.Protocol);
			lock (origin!._seen) origin._seen.Add(seen);
			await respond(context);
		});
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		origin = new TestOrigin(app, new Uri(address).Port, https ? "https" : "http", certificate);
		return origin;
	}

	public string UrlFor(string path) => $"{_scheme}://127.0.0.1:{Port}{path}";

	static X509Certificate2 CreateSelfSignedCertificate() {
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest("CN=test origin", key, HashAlgorithmName.SHA256);
		var names = new SubjectAlternativeNameBuilder();
		names.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(names.Build());
		using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
		return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
	}

	public async ValueTask DisposeAsync() {
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}

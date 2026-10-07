using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MoSaProxy.Tests;

/// <summary>
/// An HTTP/1.1 origin that answers each request with exactly the bytes a test hands it (odd header casing, chunk extensions and
/// all), so that a test can compare what the origin wrote with what the proxy captured. Keep-alive; optionally TLS.
/// </summary>
public sealed class RawOrigin : IAsyncDisposable {
	readonly TcpListener _listener;
	readonly Func<string, byte[]> _reply;
	readonly X509Certificate2? _certificate;
	readonly CancellationTokenSource _stop = new();

	RawOrigin(TcpListener listener, Func<string, byte[]> reply, X509Certificate2? certificate) {
		_listener = listener;
		_reply = reply;
		_certificate = certificate;
		Port = ((IPEndPoint)listener.LocalEndpoint).Port;
		_ = Task.Run(AcceptLoopAsync);
	}

	public int Port { get; }

	/// <param name="reply">Given the request target (path), returns the exact response bytes to write.</param>
	public static RawOrigin Start(Func<string, byte[]> reply, bool tls = false) {
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return new RawOrigin(listener, reply, tls ? SelfSigned() : null);
	}

	static X509Certificate2 SelfSigned() {
		using var key = RSA.Create(2048);
		var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var names = new SubjectAlternativeNameBuilder();
		names.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(names.Build());
		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
		return new X509Certificate2(certificate.Export(X509ContentType.Pfx));   // re-imported so the private key is usable by SslStream
	}

	async Task AcceptLoopAsync() {
		try {
			while (!_stop.IsCancellationRequested) {
				var client = await _listener.AcceptTcpClientAsync(_stop.Token);
				_ = Task.Run(() => ServeAsync(client));
			}
		}
		catch (OperationCanceledException) { }
		catch (ObjectDisposedException) { }
		catch (SocketException) { }
	}

	async Task ServeAsync(TcpClient client) {
		using var _ = client;
		try {
			Stream stream = client.GetStream();
			if (_certificate is not null) {
				var tls = new SslStream(stream);
				await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
					ServerCertificate = _certificate,
					ApplicationProtocols = [SslApplicationProtocol.Http11],
				}, _stop.Token);
				stream = tls;
			}
			var received = new List<byte>();
			var buffer = new byte[4096];
			while (true) {
				if (Http1Framing.FindRequest(received.ToArray(), 0) is { Complete: true } request) {
					string target = request.StartLine.Split(' ')[1];
					received.RemoveRange(0, request.Length);
					await stream.WriteAsync(_reply(target), _stop.Token);
					continue;
				}
				int n = await stream.ReadAsync(buffer, _stop.Token);
				if (n == 0) return;
				received.AddRange(buffer.AsSpan(0, n).ToArray());
			}
		}
		catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or System.Security.Authentication.AuthenticationException or SocketException) { }
	}

	public ValueTask DisposeAsync() {
		_stop.Cancel();
		_listener.Stop();
		return ValueTask.CompletedTask;
	}
}

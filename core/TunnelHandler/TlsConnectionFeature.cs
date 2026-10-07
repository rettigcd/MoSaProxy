using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;

namespace MoSaProxy;

/// <summary>Marks a connection as TLS for Kestrel (there is never a client certificate: the proxy does not ask for one).</summary>
sealed class TlsConnectionFeature : ITlsConnectionFeature {
	public X509Certificate2? ClientCertificate { get; set; }
	public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate);
}

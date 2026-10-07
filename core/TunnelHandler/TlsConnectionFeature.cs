using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace MoSaProxy;

/// <summary>Marks a connection as TLS for Kestrel (there is never a client certificate: the proxy does not ask for one).</summary>
sealed class TlsConnectionFeature : ITlsConnectionFeature {
	public X509Certificate2? ClientCertificate { get; set; }
	public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate);
}

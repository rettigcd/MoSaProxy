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

/// <summary>Tells Kestrel which protocol the TLS handshake negotiated ("h2" or "http/1.1").</summary>
sealed class TlsApplicationProtocolFeature(ReadOnlyMemory<byte> protocol) : ITlsApplicationProtocolFeature {
	public ReadOnlyMemory<byte> ApplicationProtocol { get; } = protocol;
}

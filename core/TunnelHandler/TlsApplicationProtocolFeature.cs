using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace MoSaProxy;

/// <summary>Tells Kestrel which protocol the TLS handshake negotiated ("h2" or "http/1.1").</summary>
sealed class TlsApplicationProtocolFeature(ReadOnlyMemory<byte> protocol) : ITlsApplicationProtocolFeature {
	public ReadOnlyMemory<byte> ApplicationProtocol { get; } = protocol;
}

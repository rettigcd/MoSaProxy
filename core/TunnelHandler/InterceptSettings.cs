namespace MoSaProxy;

/// <summary>
/// Whether CONNECT tunnels are decrypted, and for which hosts, as it stands right now. <see cref="TunnelHandler"/> asks it for every
/// CONNECT, so a change applies to the next tunnel; a tunnel already open keeps the mode it began with. The options only give the
/// starting values.
/// </summary>
public sealed class InterceptSettings(bool enabled, IReadOnlyList<string> bypass, CertificateAuthority? authority) {
	volatile bool _enabled = enabled;
	volatile IReadOnlyList<string> _bypass = bypass;
	volatile CertificateAuthority? _authority = authority;

	/// <summary>Whether tunnels are decrypted. Only true when there is a <see cref="Authority"/> to decrypt with.</summary>
	public bool Enabled { get => _enabled && _authority is not null; internal set => _enabled = value; }

	/// <summary>The certificate authority that signs the certificates shown to clients. Null until interception is first needed; once made it is kept.</summary>
	public CertificateAuthority? Authority { get => _authority; internal set => _authority = value; }

	/// <summary>Hosts that are never decrypted: an exact host name, or <c>*.suffix</c> for every host ending in it.</summary>
	public IReadOnlyList<string> Bypass { get => _bypass; internal set => _bypass = value; }

	/// <summary>Whether a tunnel to <paramref name="host"/> that is being opened now should be decrypted.</summary>
	public bool ShouldIntercept(string host) => Enabled && !_bypass.Any(pattern => TunnelHandler.Matches(pattern, host));
}

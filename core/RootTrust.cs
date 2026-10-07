using System.Security.Cryptography.X509Certificates;

namespace MoSaProxy;

/// <summary>A store of trusted root certificates (on Windows, the current user's "Trusted Root Certification Authorities").</summary>
public interface IRootStore {
	/// <summary>Thumbprints of the installed root certificates whose subject is exactly <paramref name="subject"/>.</summary>
	IReadOnlyList<string> FindThumbprintsBySubject(string subject);

	/// <summary>Installs a certificate (public part only) as a trusted root. May show an operating system confirmation dialog.</summary>
	void Add(X509Certificate2 certificate);

	/// <summary>Removes the root certificate with this thumbprint, if present. May show an operating system confirmation dialog.</summary>
	void Remove(string thumbprint);
}

/// <summary>The current Windows user's trusted root store. No administrator rights are needed; Windows asks the user to confirm changes.</summary>
public sealed class WindowsUserRootStore : IRootStore {
	public IReadOnlyList<string> FindThumbprintsBySubject(string subject) {
		using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadOnly);
		return store.Certificates.Cast<X509Certificate2>().Where(c => c.Subject == subject).Select(c => c.Thumbprint).ToList();
	}

	public void Add(X509Certificate2 certificate) {
		using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadWrite);
		store.Add(certificate);
	}

	public void Remove(string thumbprint) {
		using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
		store.Open(OpenFlags.ReadWrite);
		foreach (var certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false))
			store.Remove(certificate);
	}
}

public enum RootTrustState {
	/// <summary>This CA's root is installed as a trusted root.</summary>
	Trusted,
	/// <summary>No MoSaProxy root is installed.</summary>
	NotTrusted,
	/// <summary>A MoSaProxy root from a different CA folder is installed, but not this one.</summary>
	OtherMoSaProxyRootInstalled,
}

/// <summary>Checks, installs and removes the MoSaProxy root certificate in a root store.</summary>
public sealed class RootTrust(IRootStore store) {

	public RootTrustState Check(CertificateAuthority authority) {
		var installed = store.FindThumbprintsBySubject(CertificateAuthority.RootSubject);
		if (installed.Any(t => string.Equals(t, authority.Root.Thumbprint, StringComparison.OrdinalIgnoreCase))) return RootTrustState.Trusted;
		return installed.Count > 0 ? RootTrustState.OtherMoSaProxyRootInstalled : RootTrustState.NotTrusted;
	}

	/// <summary>
	/// Installs this CA's root. With <paramref name="replaceOtherMoSaProxyRoots"/>, MoSaProxy roots from other CA folders are removed afterwards
	/// (so a refused confirmation never leaves the user with no root at all).
	/// </summary>
	public void Install(CertificateAuthority authority, bool replaceOtherMoSaProxyRoots) {
		// Only the public certificate goes into the store; the private key never leaves the CA folder.
		using var publicOnly = X509CertificateLoader.LoadCertificate(authority.Root.RawData);
		store.Add(publicOnly);
		if (!replaceOtherMoSaProxyRoots) return;
		foreach (var thumbprint in store.FindThumbprintsBySubject(CertificateAuthority.RootSubject))
			if (!string.Equals(thumbprint, authority.Root.Thumbprint, StringComparison.OrdinalIgnoreCase)) store.Remove(thumbprint);
	}

	/// <summary>Removes every MoSaProxy root (whatever CA folder it came from). Returns how many were removed.</summary>
	public int RemoveAll() {
		var installed = store.FindThumbprintsBySubject(CertificateAuthority.RootSubject);
		foreach (var thumbprint in installed) store.Remove(thumbprint);
		return installed.Count;
	}
}

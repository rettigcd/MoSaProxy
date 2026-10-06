using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace mos;

/// <summary>
/// A local root certificate authority used to issue per-host certificates for TLS interception.
/// The root is created once and saved to a folder; clients must be told to trust it (see <see cref="RootCertificatePath"/>).
/// Anyone holding the root's private key can impersonate any site to a client that trusts it, so the folder
/// must stay private and the root must only be trusted on test browsers or profiles.
/// </summary>
public sealed class CertificateAuthority {
	/// <summary>The subject of every mos root certificate. Used to find installed copies of it in a trust store.</summary>
	public const string RootSubject = "CN=mos Proxy Root CA (local test CA), O=mos";

	const string RootFileName = "mos-root-ca.pfx";
	const string RootPublicFileName = "mos-root-ca.cer";

	readonly ConcurrentDictionary<string, Lazy<X509Certificate2>> _leaves = new(StringComparer.OrdinalIgnoreCase);

	CertificateAuthority(X509Certificate2 root, string directory) {
		Root = root;
		Directory = directory;
		RootCertificatePath = Path.Combine(directory, RootPublicFileName);
	}

	/// <summary>The root certificate, including its private key.</summary>
	public X509Certificate2 Root { get; }

	/// <summary>The folder holding the root certificate files.</summary>
	public string Directory { get; }

	/// <summary>Path of the root certificate without its private key (.cer), for installing into a trust store.</summary>
	public string RootCertificatePath { get; }

	/// <summary>Loads the root from <paramref name="directory"/>, creating and saving a new one if there is none.</summary>
	public static CertificateAuthority LoadOrCreate(string directory) {
		System.IO.Directory.CreateDirectory(directory);
		string pfxPath = Path.Combine(directory, RootFileName);

		X509Certificate2 root;
		if (File.Exists(pfxPath)) {
			root = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, null, X509KeyStorageFlags.Exportable);
		}
		else {
			root = CreateRoot();
			File.WriteAllBytes(pfxPath, root.Export(X509ContentType.Pfx));
			RestrictToCurrentUser(pfxPath);
			File.WriteAllBytes(Path.Combine(directory, RootPublicFileName), root.Export(X509ContentType.Cert));
		}

		var authority = new CertificateAuthority(root, directory);
		if (!File.Exists(authority.RootCertificatePath))
			File.WriteAllBytes(authority.RootCertificatePath, root.Export(X509ContentType.Cert));
		return authority;
	}

	/// <summary>Returns a certificate for <paramref name="host"/> (DNS name or IP address) signed by the root. Cached per host.</summary>
	public X509Certificate2 GetCertificateFor(string host) =>
		_leaves.GetOrAdd(host, h => new Lazy<X509Certificate2>(() => CreateLeaf(h))).Value;

	static X509Certificate2 CreateRoot() {
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest(RootSubject, key, HashAlgorithmName.SHA256);
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
		request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
		request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
		using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
		// Round-trip through PFX so the private key is usable by the TLS stack on every platform.
		return X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
	}

	X509Certificate2 CreateLeaf(string host) {
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string commonName = host.Length <= 60 ? host : "mos-generated";
		var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);

		var names = new SubjectAlternativeNameBuilder();
		if (IPAddress.TryParse(host, out var ip)) names.AddIpAddress(ip);
		else names.AddDnsName(host);
		request.CertificateExtensions.Add(names.Build());
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
		request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
		request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
		request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
		request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Root, true, false));

		var serial = RandomNumberGenerator.GetBytes(16);
		serial[0] &= 0x7F; // keep the serial number positive
		using var signed = request.Create(Root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90), serial);
		using var withKey = signed.CopyWithPrivateKey(key);
		return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
	}

	static void RestrictToCurrentUser(string path) {
		if (!OperatingSystem.IsWindows())
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		// On Windows the default location is under the user's profile folder, which other users cannot read.
	}
}

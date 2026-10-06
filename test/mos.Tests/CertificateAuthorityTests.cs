using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shouldly;

namespace mos.Tests;

public class CertificateAuthorityTests {

	static bool ChainsToRoot(X509Certificate2 certificate, X509Certificate2 root) {
		using var chain = new X509Chain();
		chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
		chain.ChainPolicy.CustomTrustStore.Add(root);
		chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
		return chain.Build(certificate);
	}

	[Fact]
	public void LoadOrCreate_EmptyFolder_CreatesARootCertificateAndSavesIt() {

		// Given: an empty folder
		using var folder = new TempFolder();

		// When: the certificate authority is loaded from it
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// Then: a root certificate that can sign others was created, with its private key file and a public .cer file saved
		authority.Root.HasPrivateKey.ShouldBeTrue();
		authority.Root.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority.ShouldBeTrue();
		authority.Root.Subject.ShouldContain("mos");
		File.Exists(Path.Combine(folder.Path, "mos-root-ca.pfx")).ShouldBeTrue();
		File.Exists(authority.RootCertificatePath).ShouldBeTrue();
	}

	[Fact]
	public void LoadOrCreate_FolderWithExistingRoot_LoadsTheSameRootInsteadOfMakingANewOne() {

		// Given: a folder where a certificate authority was already created
		using var folder = new TempFolder();
		var first = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: the authority is loaded from that folder again
		var second = CertificateAuthority.LoadOrCreate(folder.Path);

		// Then: it is the same root (same thumbprint) and still has its private key, so certificates issued earlier stay trusted
		second.Root.Thumbprint.ShouldBe(first.Root.Thumbprint);
		second.Root.HasPrivateKey.ShouldBeTrue();
	}

	[Fact]
	public void RootCertificateFile_PublicCopy_ContainsNoPrivateKey() {

		// Given: a newly created certificate authority
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: the shareable .cer file is loaded
		var publicCopy = X509CertificateLoader.LoadCertificateFromFile(authority.RootCertificatePath);

		// Then: it is the same certificate as the root but carries no private key
		publicCopy.Thumbprint.ShouldBe(authority.Root.Thumbprint);
		publicCopy.HasPrivateKey.ShouldBeFalse();
	}

	[Fact]
	public void GetCertificateFor_DnsName_IsSignedByTheRootAndNamesTheHost() {

		// Given: a certificate authority
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: a certificate is requested for www.example.com
		var certificate = authority.GetCertificateFor("www.example.com");

		// Then: it chains to the root, lists that DNS name, is for server authentication, is not itself a CA, and has a private key
		ChainsToRoot(certificate, authority.Root).ShouldBeTrue();
		certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames().ShouldContain("www.example.com");
		certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ShouldContain("1.3.6.1.5.5.7.3.1");
		certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority.ShouldBeFalse();
		certificate.HasPrivateKey.ShouldBeTrue();
		certificate.NotBefore.ShouldBeLessThan(DateTime.Now);
		certificate.NotAfter.ShouldBeGreaterThan(DateTime.Now.AddDays(30));
	}

	[Fact]
	public void GetCertificateFor_IpAddress_ListsTheAddressAsAnIpName() {

		// Given: a certificate authority
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: a certificate is requested for 192.0.2.7
		var certificate = authority.GetCertificateFor("192.0.2.7");

		// Then: the certificate lists 192.0.2.7 as an IP address name and chains to the root
		certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single()
			.EnumerateIPAddresses().Select(a => a.ToString()).ShouldContain("192.0.2.7");
		ChainsToRoot(certificate, authority.Root).ShouldBeTrue();
	}

	[Fact]
	public void GetCertificateFor_VeryLongHostName_StillWorks() {

		// Given: a certificate authority and a host name longer than the 64-character limit for a certificate's common name
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		string host = string.Join(".", Enumerable.Repeat("abcdefghij", 12)) + ".example.com";

		// When: a certificate is requested for it
		var certificate = authority.GetCertificateFor(host);

		// Then: a valid certificate is issued that still lists the full name
		certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames().ShouldContain(host);
	}

	[Fact]
	public void GetCertificateFor_SameHostTwice_ReturnsTheCachedCertificate() {

		// Given: a certificate authority
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: a certificate is requested twice for one host (different capitalization) and once for another host
		var first = authority.GetCertificateFor("example.com");
		var again = authority.GetCertificateFor("EXAMPLE.com");
		var other = authority.GetCertificateFor("example.org");

		// Then: the same host gets the same certificate and a different host gets a different one
		again.Thumbprint.ShouldBe(first.Thumbprint);
		other.Thumbprint.ShouldNotBe(first.Thumbprint);
	}

	[Fact]
	public void SpkiSha256Base64_Root_IsTheHashOfItsPublicKey() {

		// Given: a certificate authority
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);

		// When: its SPKI hash is read and also computed independently
		var reported = authority.SpkiSha256Base64;
		var expected = Convert.ToBase64String(SHA256.HashData(authority.Root.PublicKey.ExportSubjectPublicKeyInfo()));

		// Then: they match, and the value is a 32-byte hash in Base64 (44 characters)
		reported.ShouldBe(expected);
		reported.Length.ShouldBe(44);
	}
}

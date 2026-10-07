using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shouldly;

namespace MoSaProxy.Tests;

public class RootTrust_Tests {

	const string OtherThumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

	static CertificateAuthority NewAuthority(TempFolder folder) => CertificateAuthority.LoadOrCreate(folder.Path);

	[Fact]
	public void RootSubject_NewAuthority_MatchesTheRootCertificatesSubject() {

		// Given: a new certificate authority
		using var folder = new TempFolder();
		var authority = NewAuthority(folder);

		// When: its root certificate's subject is read

		// Then: it equals the constant used to find installed copies in a trust store
		authority.Root.Subject.ShouldBe(CertificateAuthority.RootSubject);
	}

	[Fact]
	public void Check_EmptyStore_IsNotTrusted() {

		// Given: an authority and a root store that has no MoSaProxy certificate
		using var folder = new TempFolder();
		var trust = new RootTrust(new FakeRootStore());

		// When: trust is checked
		var state = trust.Check(NewAuthority(folder));

		// Then: the root is reported as not trusted
		state.ShouldBe(RootTrustState.NotTrusted);
	}

	[Fact]
	public void Check_StoreHoldsThisRoot_IsTrusted() {

		// Given: an authority and a store where its root is installed
		using var folder = new TempFolder();
		var authority = NewAuthority(folder);
		var store = new FakeRootStore();
		store.Preinstall(authority.Root.Thumbprint.ToLowerInvariant(), CertificateAuthority.RootSubject);

		// When: trust is checked (the store happens to use lower-case thumbprints)
		var state = new RootTrust(store).Check(authority);

		// Then: the root is reported as trusted
		state.ShouldBe(RootTrustState.Trusted);
	}

	[Fact]
	public void Check_StoreHoldsADifferentMoSaProxyRoot_ReportsThatAnotherOneIsInstalled() {

		// Given: an authority and a store holding a MoSaProxy root with the same name but a different key (from an earlier CA folder)
		using var folder = new TempFolder();
		var store = new FakeRootStore();
		store.Preinstall(OtherThumbprint, CertificateAuthority.RootSubject);

		// When: trust is checked
		var state = new RootTrust(store).Check(NewAuthority(folder));

		// Then: it is reported as another MoSaProxy root being installed, not as trusted
		state.ShouldBe(RootTrustState.OtherMoSaProxyRootInstalled);
	}

	[Fact]
	public void Check_StoreHoldsUnrelatedRoots_IsNotTrusted() {

		// Given: an authority and a store holding only roots with other names
		using var folder = new TempFolder();
		var store = new FakeRootStore();
		store.Preinstall(OtherThumbprint, "CN=Some Other Root CA");

		// When: trust is checked
		var state = new RootTrust(store).Check(NewAuthority(folder));

		// Then: the root is reported as not trusted
		state.ShouldBe(RootTrustState.NotTrusted);
	}

	[Fact]
	public void Install_NewRoot_AddsOnlyThePublicCertificate() {

		// Given: an authority (whose root has a private key) and an empty store
		using var folder = new TempFolder();
		var authority = NewAuthority(folder);
		var store = new FakeRootStore();

		// When: the root is installed
		new RootTrust(store).Install(authority, replaceOtherMoSaProxyRoots: false);

		// Then: the store holds the root's thumbprint, and the copy that was added had no private key
		store.Thumbprints.ShouldBe(new[] { authority.Root.Thumbprint });
		store.AnyInstalledCopyHadAPrivateKey.ShouldBeFalse();
		authority.Root.HasPrivateKey.ShouldBeTrue();
	}

	[Fact]
	public void Install_ReplacingOtherRoots_AddsTheNewRootBeforeRemovingTheOldOne() {

		// Given: an authority and a store holding an older MoSaProxy root
		using var folder = new TempFolder();
		var authority = NewAuthority(folder);
		var store = new FakeRootStore();
		store.Preinstall(OtherThumbprint, CertificateAuthority.RootSubject);

		// When: the root is installed with replacement of other MoSaProxy roots
		new RootTrust(store).Install(authority, replaceOtherMoSaProxyRoots: true);

		// Then: the store holds only the new root, and the new one was added first (so a refused dialog never leaves no root)
		store.Thumbprints.ShouldBe(new[] { authority.Root.Thumbprint });
		store.Log.ShouldBe(new[] { "add " + authority.Root.Thumbprint, "remove " + OtherThumbprint });
	}

	[Fact]
	public void Install_AddRefused_LeavesTheOldRootAlone() {

		// Given: an authority, a store holding an older MoSaProxy root, and a user who refuses the Windows confirmation dialog
		using var folder = new TempFolder();
		var store = new FakeRootStore { AddFailure = new CryptographicException("The operation was canceled by the user.") };
		store.Preinstall(OtherThumbprint, CertificateAuthority.RootSubject);

		// When: installing with replacement is attempted
		var install = () => new RootTrust(store).Install(NewAuthority(folder), replaceOtherMoSaProxyRoots: true);

		// Then: the failure is reported to the caller and the old root was not removed
		Should.Throw<CryptographicException>(install);
		store.Thumbprints.ShouldBe(new[] { OtherThumbprint });
	}

	[Fact]
	public void RemoveAll_StoreHoldsSeveralMoSaProxyRootsAndOthers_RemovesOnlyTheMoSaProxyRoots() {

		// Given: a store with two MoSaProxy roots (different keys) and an unrelated root
		var store = new FakeRootStore();
		store.Preinstall("1111111111111111111111111111111111111111", CertificateAuthority.RootSubject);
		store.Preinstall("2222222222222222222222222222222222222222", CertificateAuthority.RootSubject);
		store.Preinstall("3333333333333333333333333333333333333333", "CN=Some Other Root CA");

		// When: all MoSaProxy roots are removed
		var removed = new RootTrust(store).RemoveAll();

		// Then: two were removed and the unrelated root is still there
		removed.ShouldBe(2);
		store.Thumbprints.ShouldBe(new[] { "3333333333333333333333333333333333333333" });
	}

	[Fact]
	public void RemoveAll_NoMoSaProxyRootInstalled_RemovesNothing() {

		// Given: an empty store
		var store = new FakeRootStore();

		// When: all MoSaProxy roots are removed
		var removed = new RootTrust(store).RemoveAll();

		// Then: nothing was removed
		removed.ShouldBe(0);
	}
}

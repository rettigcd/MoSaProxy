using Shouldly;

namespace MoSaProxy.Tests;

public class InterceptionSetup_Tests {

	const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

	static (bool Trusted, string Output) Run(CertificateAuthority authority, FakeRootStore store, string typedInput, bool interactive = true) {
		var output = new StringWriter();
		bool trusted = InterceptionSetup.Run(authority, 8080, Chrome, new RootTrust(store), new StringReader(typedInput), new ColorConsole(output), interactive);
		return (trusted, output.ToString());
	}

	[Fact]
	public void Run_RootAlreadyTrusted_DoesNotAskAndShowsTheCommandWithoutTheIgnoreFlag() {

		// Given: an authority whose root is already installed as trusted
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();
		store.Preinstall(authority.Root.Thumbprint, CertificateAuthority.RootSubject);

		// When: the startup setup runs in an interactive console
		var (trusted, output) = Run(authority, store, typedInput: "");

		// Then: it reports trusted, asks nothing, changes nothing, and prints a Chrome command without --ignore-certificate-errors
		trusted.ShouldBeTrue();
		output.ShouldNotContain("[y/N]");
		store.Log.ShouldBeEmpty();
		output.ShouldContain("--proxy-server=\"http://127.0.0.1:8080\"");
		output.ShouldNotContain("--ignore-certificate-errors");
		output.ShouldContain("--untrust-ca");
	}

	[Theory]
	[InlineData("y")]
	[InlineData("Y")]
	[InlineData("yes")]
	[InlineData("  Yes  ")]
	public void Run_NotTrustedAndUserAnswersYes_InstallsTheRootAndShowsTheCommandWithoutTheIgnoreFlag(string answer) {

		// Given: an authority whose root is not installed, and a user who answers yes
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();

		// When: the startup setup runs in an interactive console
		var (trusted, output) = Run(authority, store, answer + "\n");

		// Then: the root was installed, the user was asked first, and the printed Chrome command does not ignore certificate errors
		trusted.ShouldBeTrue();
		store.Thumbprints.ShouldBe(new[] { authority.Root.Thumbprint });
		output.ShouldContain("[y/N]");
		output.ShouldContain("Installed.");
		output.ShouldNotContain("--ignore-certificate-errors");
	}

	[Theory]
	[InlineData("n")]
	[InlineData("")]
	[InlineData("maybe")]
	public void Run_NotTrustedAndUserDoesNotAnswerYes_InstallsNothingAndShowsTheThrowawayProfileCommand(string answer) {

		// Given: an authority whose root is not installed, and a user who answers no, presses Enter, or types something else
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();

		// When: the startup setup runs in an interactive console
		var (trusted, output) = Run(authority, store, answer + "\n");

		// Then: nothing was installed (the default is No), and the printed Chrome command uses --ignore-certificate-errors
		trusted.ShouldBeFalse();
		store.Log.ShouldBeEmpty();
		output.ShouldContain("Not installed.");
		output.ShouldContain("--ignore-certificate-errors");
	}

	[Fact]
	public void Run_NotTrustedAndInputIsEnded_TreatsItAsNo() {

		// Given: an authority whose root is not installed, and an input that ends without any answer
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();

		// When: the startup setup runs and tries to read the answer
		var (trusted, _) = Run(authority, store, typedInput: "");

		// Then: nothing was installed
		trusted.ShouldBeFalse();
		store.Log.ShouldBeEmpty();
	}

	[Fact]
	public void Run_NotInteractive_NeverAsksAndNeverInstalls() {

		// Given: an authority whose root is not installed, and a console that cannot be asked (for example input is redirected)
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();

		// When: the startup setup runs with an answer of "y" waiting in the input, but interactive turned off
		var (trusted, output) = Run(authority, store, typedInput: "y\n", interactive: false);

		// Then: the "y" is ignored, nothing is installed, and the output says why and shows the throwaway profile command
		trusted.ShouldBeFalse();
		store.Log.ShouldBeEmpty();
		output.ShouldNotContain("[y/N]");
		output.ShouldContain("no interactive console");
		output.ShouldContain("--ignore-certificate-errors");
	}

	[Fact]
	public void Run_UserRefusesTheWindowsDialog_ReportsNotInstalledAndFallsBackToTheThrowawayProfile() {

		// Given: an authority whose root is not installed, a user who answers yes, but then refuses Windows' own confirmation dialog
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore { AddFailure = new System.Security.Cryptography.CryptographicException("The operation was canceled by the user.") };

		// When: the startup setup runs
		var (trusted, output) = Run(authority, store, "y\n");

		// Then: it reports that nothing was installed, including the reason, and shows the throwaway profile command
		trusted.ShouldBeFalse();
		output.ShouldContain("Not installed (The operation was canceled by the user.).");
		output.ShouldContain("--ignore-certificate-errors");
	}

	[Fact]
	public void Run_AnotherMoSaProxyRootIsInstalledAndUserAgrees_ReplacesItWithTheCurrentRoot() {

		// Given: an authority, and a store holding an older MoSaProxy root with a different key, and a user who agrees to replace it
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();
		store.Preinstall("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CertificateAuthority.RootSubject);

		// When: the startup setup runs
		var (trusted, output) = Run(authority, store, "y\n");

		// Then: the question mentions replacing, and only the current root is installed afterwards
		trusted.ShouldBeTrue();
		output.ShouldContain("Replace it");
		store.Thumbprints.ShouldBe(new[] { authority.Root.Thumbprint });
	}

	[Fact]
	public void Run_AnotherMoSaProxyRootIsInstalledAndUserDeclines_LeavesTheOldRootInPlace() {

		// Given: an authority, a store holding an older MoSaProxy root, and a user who declines to replace it
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore();
		store.Preinstall("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", CertificateAuthority.RootSubject);

		// When: the startup setup runs
		var (trusted, _) = Run(authority, store, "n\n");

		// Then: the root is reported as not trusted and the store is untouched
		trusted.ShouldBeFalse();
		store.Thumbprints.ShouldBe(new[] { "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" });
		store.Log.ShouldBeEmpty();
	}

	[Fact]
	public void Run_StoreCannotBeRead_DoesNotAskAndFallsBackToTheThrowawayProfile() {

		// Given: an authority and a root store that throws when read
		using var folder = new TempFolder();
		var authority = CertificateAuthority.LoadOrCreate(folder.Path);
		var store = new FakeRootStore { ReadFailure = new InvalidOperationException("store unavailable") };

		// When: the startup setup runs in an interactive console
		var (trusted, output) = Run(authority, store, "y\n");

		// Then: it says the store could not be read, does not ask or install, and shows the throwaway profile command
		trusted.ShouldBeFalse();
		output.ShouldContain("Could not read your trusted root store (store unavailable)");
		output.ShouldNotContain("[y/N]");
		output.ShouldContain("--ignore-certificate-errors");
	}
}

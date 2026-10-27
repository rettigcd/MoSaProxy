using Shouldly;

namespace MoSaProxy.Tests;

public class ChromeLauncher_Tests {

	const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

	[Fact]
	public void PowerShellCommand_IgnoringCertificateErrors_UsesPowerShellSyntaxAndTheIgnoreFlag() {

		// Given: a Chrome path with spaces and a proxy port
		// (supplied by the constants above)

		// When: the PowerShell command is built for a throwaway profile that ignores certificate errors
		var command = ChromeLauncher.PowerShellCommand(Chrome, 8080, ignoreCertificateErrors: true);

		// Then: it runs the quoted path with "&", uses $env:TEMP (not %TEMP%), points at the proxy port, and ends with the ignore flag
		command.ShouldStartWith($"& \"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"$env:TEMP\\chrome-mosaproxy\"");
		command.ShouldNotContain("%TEMP%");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:8080\"");
		command.ShouldEndWith(" --ignore-certificate-errors");
	}

	[Fact]
	public void PowerShellCommand_WhenTheRootIsTrusted_HasNoIgnoreFlag() {

		// Given: a Chrome path and a proxy port
		// (supplied by the constants above)

		// When: the PowerShell command is built for a machine where Windows already trusts the MoSaProxy root
		var command = ChromeLauncher.PowerShellCommand(Chrome, 8080, ignoreCertificateErrors: false);

		// Then: the command ends with the proxy setting and does not switch off certificate checking
		command.ShouldEndWith("--proxy-server=\"http://127.0.0.1:8080\"");
		command.ShouldNotContain("ignore-certificate-errors");
	}

	[Fact]
	public void CmdCommand_IgnoringCertificateErrors_UsesTheQuotedPathPercentTempAndTheIgnoreFlag() {

		// Given: a Chrome path with spaces and a proxy port
		// (supplied by the constants above)

		// When: the Command Prompt command is built for a throwaway profile that ignores certificate errors
		var command = ChromeLauncher.CmdCommand(Chrome, 9090, ignoreCertificateErrors: true);

		// Then: it starts with the quoted path (no "&"), uses %TEMP%, points at the proxy port, and ends with the ignore flag
		command.ShouldStartWith($"\"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"%TEMP%\\chrome-mosaproxy\"");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:9090\"");
		command.ShouldEndWith(" --ignore-certificate-errors");
	}

	[Fact]
	public void CmdCommand_WhenTheRootIsTrusted_HasNoIgnoreFlag() {

		// Given: a Chrome path and a proxy port
		// (supplied by the constants above)

		// When: the Command Prompt command is built for a machine where Windows already trusts the MoSaProxy root
		var command = ChromeLauncher.CmdCommand(Chrome, 9090, ignoreCertificateErrors: false);

		// Then: the command ends with the proxy setting and does not switch off certificate checking
		command.ShouldEndWith("--proxy-server=\"http://127.0.0.1:9090\"");
		command.ShouldNotContain("ignore-certificate-errors");
	}

	[Fact]
	public void FindChrome_Called_ReturnsAChromeExePath() {

		// Given: a machine that may or may not have Chrome installed

		// When: Chrome is looked for
		var path = ChromeLauncher.FindChrome();

		// Then: the result is a path to chrome.exe (an existing install, or the default location if none was found)
		Path.GetFileName(path).ShouldBe("chrome.exe");
	}

	[Fact]
	public void Arguments_ForTheProxy_UseTheThrowawayProfileAndTheProxyPort() {

		// Given: a proxy port, a trusted root (no need to ignore certificate errors) and no page
		// (supplied below)

		// When: the arguments for starting Chrome are built
		var arguments = ChromeLauncher.Arguments(8080, ignoreCertificateErrors: false, url: null);

		// Then: they name the throwaway profile in the temp folder and the proxy, skip the first-run prompts, and neither ignore certificates nor name a page
		arguments.ShouldContain($"--user-data-dir={Path.Combine(Path.GetTempPath(), "chrome-mosaproxy")}");
		arguments.ShouldContain("--proxy-server=http://127.0.0.1:8080");
		arguments.ShouldContain("--no-first-run");
		arguments.ShouldContain("--no-default-browser-check");
		arguments.ShouldNotContain("--ignore-certificate-errors");
		arguments.Count.ShouldBe(4);
	}

	[Fact]
	public void Arguments_IgnoringCertificateErrorsWithAPage_AddsTheFlagAndEndsWithThePage() {

		// Given: a proxy port, an untrusted root and a page to open
		// (supplied below)

		// When: the arguments for starting Chrome are built
		var arguments = ChromeLauncher.Arguments(9000, ignoreCertificateErrors: true, url: "https://example.com/");

		// Then: the ignore flag is included and the page is last, where Chrome expects it
		arguments.ShouldContain("--ignore-certificate-errors");
		arguments[^1].ShouldBe("https://example.com/");
	}

	[Fact]
	public void TryStart_ChromeIsNotThere_ReportsWhereItLooked() {

		// Given: a path where there is no Chrome
		string missing = Path.Combine(Path.GetTempPath(), "no-such-folder", "chrome.exe");

		// When: starting it is attempted
		bool started = ChromeLauncher.TryStart(missing, 8080, ignoreCertificateErrors: false, url: null, out var error);

		// Then: nothing is started, and the error names the path
		started.ShouldBeFalse();
		error.ShouldContain(missing);
	}
}

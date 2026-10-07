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
}

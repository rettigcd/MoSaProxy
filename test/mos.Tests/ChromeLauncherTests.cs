using Shouldly;

namespace mos.Tests;

public class ChromeLauncherTests {

	const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
	const string Hash = "QiuK1PA9YBPwrEhct7BhpjGYM1NReqAyHNxcqD7OINo=";

	[Fact]
	public void PowerShellCommand_Built_StartsWithTheCallOperatorAndUsesPowerShellSyntax() {

		// Given: a Chrome path with spaces, a proxy port and an SPKI hash that ends in "="
		// (supplied by the constants above)

		// When: the PowerShell command is built
		var command = ChromeLauncher.PowerShellCommand(Chrome, 8080, Hash);

		// Then: it runs the quoted path with "&", uses $env:TEMP (not %TEMP%), points at the proxy port, and quotes the hash argument whole
		command.ShouldStartWith($"& \"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"$env:TEMP\\chrome-mos\"");
		command.ShouldNotContain("%TEMP%");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:8080\"");
		command.ShouldEndWith($"\"--ignore-certificate-errors-spki-list={Hash}\"");
	}

	[Fact]
	public void CmdCommand_Built_UsesTheQuotedPathAndPercentTempSyntax() {

		// Given: a Chrome path with spaces, a proxy port and an SPKI hash
		// (supplied by the constants above)

		// When: the Command Prompt command is built
		var command = ChromeLauncher.CmdCommand(Chrome, 9090, Hash);

		// Then: it starts with the quoted path (no "&"), uses %TEMP%, points at the proxy port, and carries the hash
		command.ShouldStartWith($"\"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"%TEMP%\\chrome-mos\"");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:9090\"");
		command.ShouldEndWith($"\"--ignore-certificate-errors-spki-list={Hash}\"");
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

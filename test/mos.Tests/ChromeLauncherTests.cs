using Shouldly;

namespace mos.Tests;

public class ChromeLauncherTests {

	const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

	[Fact]
	public void PowerShellCommand_Built_StartsWithTheCallOperatorAndUsesPowerShellSyntax() {

		// Given: a Chrome path with spaces and a proxy port
		// (supplied by the constants above)

		// When: the PowerShell command is built
		var command = ChromeLauncher.PowerShellCommand(Chrome, 8080);

		// Then: it runs the quoted path with "&", uses $env:TEMP (not %TEMP%), points at the proxy port, and ends with the flag that makes the test profile accept the proxy's certificates
		command.ShouldStartWith($"& \"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"$env:TEMP\\chrome-mos\"");
		command.ShouldNotContain("%TEMP%");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:8080\"");
		command.ShouldEndWith(" --ignore-certificate-errors");
	}

	[Fact]
	public void CmdCommand_Built_UsesTheQuotedPathAndPercentTempSyntax() {

		// Given: a Chrome path with spaces and a proxy port
		// (supplied by the constants above)

		// When: the Command Prompt command is built
		var command = ChromeLauncher.CmdCommand(Chrome, 9090);

		// Then: it starts with the quoted path (no "&"), uses %TEMP%, points at the proxy port, and ends with that same flag
		command.ShouldStartWith($"\"{Chrome}\" ");
		command.ShouldContain("--user-data-dir=\"%TEMP%\\chrome-mos\"");
		command.ShouldContain("--proxy-server=\"http://127.0.0.1:9090\"");
		command.ShouldEndWith(" --ignore-certificate-errors");
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

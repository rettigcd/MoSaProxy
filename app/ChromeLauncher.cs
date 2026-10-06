namespace mos;

/// <summary>Builds ready-to-paste commands that start a test Chrome profile that uses the proxy and trusts the mos root.</summary>
public static class ChromeLauncher {
	const string DefaultPath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

	/// <summary>Finds chrome.exe in its usual install locations, or returns the default 64-bit location if none exists.</summary>
	public static string FindChrome() {
		var candidates = new[] {
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
		};
		return candidates.FirstOrDefault(File.Exists) ?? DefaultPath;
	}

	/// <summary>A one-line command for PowerShell (needs "&" to run a quoted path, and $env:TEMP instead of %TEMP%).</summary>
	public static string PowerShellCommand(string chromePath, int proxyPort, string spkiHash) =>
		$"& \"{chromePath}\" --user-data-dir=\"$env:TEMP\\chrome-mos\" --proxy-server=\"http://127.0.0.1:{proxyPort}\" \"--ignore-certificate-errors-spki-list={spkiHash}\"";

	/// <summary>A one-line command for Command Prompt (uses %TEMP%).</summary>
	public static string CmdCommand(string chromePath, int proxyPort, string spkiHash) =>
		$"\"{chromePath}\" --user-data-dir=\"%TEMP%\\chrome-mos\" --proxy-server=\"http://127.0.0.1:{proxyPort}\" \"--ignore-certificate-errors-spki-list={spkiHash}\"";
}

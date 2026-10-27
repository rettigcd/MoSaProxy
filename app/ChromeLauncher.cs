namespace MoSaProxy;

/// <summary>Builds ready-to-paste commands that start a throwaway Chrome profile that uses the proxy.</summary>
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

	/// <summary>
	/// A one-line command for PowerShell (needs "&amp;" to run a quoted path, and $env:TEMP instead of %TEMP%).
	/// With <paramref name="ignoreCertificateErrors"/> the throwaway profile ignores certificate errors, so it should only be
	/// used for browsing through the proxy; leave it off when the MoSaProxy root is trusted by Windows.
	/// (Chrome's --ignore-certificate-errors-spki-list is not used: Chrome only matches it against certificates the server
	/// actually sends, and a self-signed root is never sent.)
	/// </summary>
	public static string PowerShellCommand(string chromePath, int proxyPort, bool ignoreCertificateErrors) =>
		$"& \"{chromePath}\" --user-data-dir=\"$env:TEMP\\chrome-mosaproxy\" --proxy-server=\"http://127.0.0.1:{proxyPort}\"" + IgnoreFlag(ignoreCertificateErrors);

	/// <summary>A one-line command for Command Prompt (uses %TEMP%).</summary>
	public static string CmdCommand(string chromePath, int proxyPort, bool ignoreCertificateErrors) =>
		$"\"{chromePath}\" --user-data-dir=\"%TEMP%\\chrome-mosaproxy\" --proxy-server=\"http://127.0.0.1:{proxyPort}\"" + IgnoreFlag(ignoreCertificateErrors);

	/// <summary>
	/// The arguments that start the same throwaway Chrome profile as the printed commands, plus two that skip the first-run and
	/// default-browser prompts a fresh profile would show, and the page to open first (if any).
	/// </summary>
	public static IReadOnlyList<string> Arguments(int proxyPort, bool ignoreCertificateErrors, string? url) {
		var arguments = new List<string> {
			$"--user-data-dir={Path.Combine(Path.GetTempPath(), "chrome-mosaproxy")}",
			$"--proxy-server=http://127.0.0.1:{proxyPort}",
			"--no-first-run",
			"--no-default-browser-check",
		};
		if (ignoreCertificateErrors) arguments.Add("--ignore-certificate-errors");
		if (!string.IsNullOrWhiteSpace(url)) arguments.Add(url);
		return arguments;
	}

	/// <summary>Starts Chrome with <see cref="Arguments"/> and does not wait for it. Returns false, with the reason, if it could not be started.</summary>
	public static bool TryStart(string chromePath, int proxyPort, bool ignoreCertificateErrors, string? url, out string? error) {
		error = null;
		if (!File.Exists(chromePath)) { error = $"Chrome was not found at {chromePath}"; return false; }
		try {
			var start = new System.Diagnostics.ProcessStartInfo(chromePath) { UseShellExecute = false };
			foreach (var argument in Arguments(proxyPort, ignoreCertificateErrors, url)) start.ArgumentList.Add(argument);
			System.Diagnostics.Process.Start(start)?.Dispose();
			return true;
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
			error = ex.Message;
			return false;
		}
	}

	static string IgnoreFlag(bool ignoreCertificateErrors) => ignoreCertificateErrors ? " --ignore-certificate-errors" : "";
}

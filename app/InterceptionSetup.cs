namespace MoSaProxy;

/// <summary>
/// The startup help for HTTPS interception: checks whether the MoSaProxy root is trusted, offers to install it,
/// and prints the Chrome command that matches the result.
/// </summary>
public static class InterceptionSetup {

	/// <summary>Returns true when the root is trusted after this runs.</summary>
	/// <param name="interactive">Whether it is allowed to ask questions (false when input is redirected or the OS has no supported store).</param>
	public static bool Run(CertificateAuthority authority, int proxyPort, string chromePath, RootTrust trust, TextReader input, ColorConsole output, bool interactive) {
		output.WriteLine("");
		output.WriteLine($"{Fg.Cyan}HTTPS interception is ON.{Fg.Restore}");
		output.WriteLine($"  Root certificate: {authority.RootCertificatePath}");
		output.WriteLine($"  {Fg.Yellow}Keep that folder private: its private key can impersonate any website to a client that trusts it.{Fg.Restore}");

		RootTrustState state;
		try { state = trust.Check(authority); }
		catch (Exception ex) {
			output.WriteLine($"  {Fg.Red}Could not read your trusted root store ({ex.Message}); assuming the root is not trusted.{Fg.Restore}");
			state = RootTrustState.NotTrusted;
			interactive = false;
		}

		bool trusted = state == RootTrustState.Trusted;
		if (!trusted && interactive) trusted = OfferInstall(authority, state, trust, input, output);
		else if (!trusted) output.WriteLine($"  {Fg.Yellow}The root is not trusted by this Windows user, and there is no interactive console to ask.{Fg.Restore}");

		if (trusted) {
			output.WriteLine($"  {Fg.Green}The root is trusted for your Windows user, so browsers accept the proxy's HTTPS certificates.{Fg.Restore} Start Chrome with:");
			PrintCommands(chromePath, proxyPort, ignoreCertificateErrors: false, output);
			output.WriteLine("  To remove the trust again, run MoSaProxy with --untrust-ca (for example: dotnet run --project app -- --untrust-ca).");
		}
		else {
			output.WriteLine("  Option 1: start a throwaway test Chrome that accepts the proxy's certificates. It ignores ALL certificate errors,");
			output.WriteLine("  so use that window only for browsing through this proxy (Chrome shows a warning bar and marks pages \"Not secure\").");
			PrintCommands(chromePath, proxyPort, ignoreCertificateErrors: true, output);
			output.WriteLine("  Option 2: restart MoSaProxy and answer y when asked to trust the root, so every browser accepts it.");
		}
		output.WriteLine("");
		return trusted;
	}

	static bool OfferInstall(CertificateAuthority authority, RootTrustState state, RootTrust trust, TextReader input, ColorConsole output) {
		bool replace = state == RootTrustState.OtherMoSaProxyRootInstalled;
		output.WriteLine("");
		if (replace) {
			output.WriteLine($"  {Fg.Yellow}A different MoSaProxy root certificate (from an earlier CA folder) is installed as trusted, but it does not match this one.{Fg.Restore}");
			output.WriteLine("  Replace it with the current one? That would trust the current root, and remove the old one.");
		}
		else {
			output.WriteLine($"  {Fg.Yellow}The MoSaProxy root certificate is not trusted by this Windows user, so browsers will show certificate errors for HTTPS pages.{Fg.Restore}");
			output.WriteLine("  Trust it for your Windows user? Every app on this account will then trust certificates signed by the key in that folder.");
		}
		output.WriteLine("  Windows will show a confirmation dialog.");
		output.Write($"  Install it now? {Fg.White}[y/N]{Fg.Restore} ");

		var answer = input.ReadLine()?.Trim();
		if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) {
			output.WriteLine("  Not installed.");
			return false;
		}

		try {
			trust.Install(authority, replaceOtherMoSaProxyRoots: replace);
			output.WriteLine($"  {Fg.Green}Installed.{Fg.Restore}");
			return true;
		}
		catch (Exception ex) {
			output.WriteLine($"  {Fg.Red}Not installed ({ex.Message.Trim()}).{Fg.Restore}");
			return false;
		}
	}

	static void PrintCommands(string chromePath, int proxyPort, bool ignoreCertificateErrors, ColorConsole output) {
		output.WriteLine("  In PowerShell:");
		output.WriteLine($"    {Fg.Cyan}{ChromeLauncher.PowerShellCommand(chromePath, proxyPort, ignoreCertificateErrors)}{Fg.Restore}");
		output.WriteLine("  In Command Prompt:");
		output.WriteLine($"    {Fg.Cyan}{ChromeLauncher.CmdCommand(chromePath, proxyPort, ignoreCertificateErrors)}{Fg.Restore}");
	}
}

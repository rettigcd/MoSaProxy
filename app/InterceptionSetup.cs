namespace mos;

/// <summary>
/// The startup help for HTTPS interception: checks whether the mos root is trusted, offers to install it,
/// and prints the Chrome command that matches the result.
/// </summary>
public static class InterceptionSetup {

	/// <summary>Returns true when the root is trusted after this runs.</summary>
	/// <param name="interactive">Whether it is allowed to ask questions (false when input is redirected or the OS has no supported store).</param>
	public static bool Run(CertificateAuthority authority, int proxyPort, string chromePath, RootTrust trust, TextReader input, TextWriter output, bool interactive) {
		output.WriteLine();
		output.WriteLine("HTTPS interception is ON.");
		output.WriteLine($"  Root certificate: {authority.RootCertificatePath}");
		output.WriteLine("  Keep that folder private: its private key can impersonate any website to a client that trusts it.");

		RootTrustState state;
		try { state = trust.Check(authority); }
		catch (Exception ex) {
			output.WriteLine($"  Could not read your trusted root store ({ex.Message}); assuming the root is not trusted.");
			state = RootTrustState.NotTrusted;
			interactive = false;
		}

		bool trusted = state == RootTrustState.Trusted;
		if (!trusted && interactive) trusted = OfferInstall(authority, state, trust, input, output);
		else if (!trusted) output.WriteLine("  The root is not trusted by this Windows user, and there is no interactive console to ask.");

		if (trusted) {
			output.WriteLine("  The root is trusted for your Windows user, so browsers accept the proxy's HTTPS certificates. Start Chrome with:");
			PrintCommands(chromePath, proxyPort, ignoreCertificateErrors: false, output);
			output.WriteLine("  To remove the trust again, run mos with --untrust-ca (for example: dotnet run --project app -- --untrust-ca).");
		}
		else {
			output.WriteLine("  Option 1: start a throwaway test Chrome that accepts the proxy's certificates. It ignores ALL certificate errors,");
			output.WriteLine("  so use that window only for browsing through this proxy (Chrome shows a warning bar and marks pages \"Not secure\").");
			PrintCommands(chromePath, proxyPort, ignoreCertificateErrors: true, output);
			output.WriteLine("  Option 2: restart mos and answer y when asked to trust the root, so every browser accepts it.");
		}
		output.WriteLine();
		return trusted;
	}

	static bool OfferInstall(CertificateAuthority authority, RootTrustState state, RootTrust trust, TextReader input, TextWriter output) {
		bool replace = state == RootTrustState.OtherMosRootInstalled;
		output.WriteLine();
		if (replace) {
			output.WriteLine("  A different mos root certificate (from an earlier CA folder) is installed as trusted, but it does not match this one.");
			output.WriteLine("  Replace it with the current one? That would trust the current root, and remove the old one.");
		}
		else {
			output.WriteLine("  The mos root certificate is not trusted by this Windows user, so browsers will show certificate errors for HTTPS pages.");
			output.WriteLine("  Trust it for your Windows user? Every app on this account will then trust certificates signed by the key in that folder.");
		}
		output.WriteLine("  Windows will show a confirmation dialog.");
		output.Write("  Install it now? [y/N] ");

		var answer = input.ReadLine()?.Trim();
		if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) {
			output.WriteLine("  Not installed.");
			return false;
		}

		try {
			trust.Install(authority, replaceOtherMosRoots: replace);
			output.WriteLine("  Installed.");
			return true;
		}
		catch (Exception ex) {
			output.WriteLine($"  Not installed ({ex.Message.Trim()}).");
			return false;
		}
	}

	static void PrintCommands(string chromePath, int proxyPort, bool ignoreCertificateErrors, TextWriter output) {
		output.WriteLine("  In PowerShell:");
		output.WriteLine($"    {ChromeLauncher.PowerShellCommand(chromePath, proxyPort, ignoreCertificateErrors)}");
		output.WriteLine("  In Command Prompt:");
		output.WriteLine($"    {ChromeLauncher.CmdCommand(chromePath, proxyPort, ignoreCertificateErrors)}");
	}
}

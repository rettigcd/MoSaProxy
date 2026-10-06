using mos;

ProxyOptions options;
try { options = ProxyOptions.Parse(args); }
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
	return 1;
}

if (options.UntrustCa) {
	if (!OperatingSystem.IsWindows()) {
		Console.Error.WriteLine("--untrust-ca is only supported on Windows.");
		return 1;
	}
	try {
		int removed = new RootTrust(new WindowsUserRootStore()).RemoveAll();
		Console.WriteLine(removed == 0
			? "No mos root certificate was installed as trusted for your Windows user."
			: $"Removed {removed} mos root certificate(s) from your Windows user's trusted roots.");
		return 0;
	}
	catch (Exception ex) {
		Console.Error.WriteLine($"Could not remove the mos root certificate: {ex.Message}");
		return 1;
	}
}

if (options.ExportSazFolder is not null) {
	string output = options.ExportSazOutput ?? SazExporter.DefaultOutputPath(options.ExportSazFolder);
	return TryExportSaz(options.ExportSazFolder, output) ? 0 : 1;
}

var sinks = new List<IExchangeLog> { new ConsoleExchangeLog() };
if (options.LogPath is not null) sinks.Add(new JsonLinesExchangeLog(options.LogPath));

var proxy = await ProxyHost.StartAsync(options, new CompositeExchangeLog(sinks.ToArray()));
try {
	Console.WriteLine($"HTTP proxy listening on 127.0.0.1:{proxy.Port}. Press Ctrl+C to stop.");
	if (proxy.CaptureRunDirectory is not null) Console.WriteLine($"Capturing sessions to {proxy.CaptureRunDirectory}");
	if (proxy.Authority is { } authority) {
		bool interactive = OperatingSystem.IsWindows() && !Console.IsInputRedirected;
		InterceptionSetup.Run(authority, proxy.Port, ChromeLauncher.FindChrome(), new RootTrust(new WindowsUserRootStore()), Console.In, ColorConsole.Default, interactive);
	}
	await proxy.WaitForShutdownAsync();
}
finally {
	// Stopping first lets exchanges that are still running finish writing their files before they are packed.
	await proxy.DisposeAsync();
}

if (options.SazOnShutdown && proxy.CaptureRunDirectory is { } runDirectory)
	TryExportSaz(runDirectory, SazExporter.DefaultOutputPath(runDirectory));
return 0;

static bool TryExportSaz(string runDirectory, string outputPath) {
	try {
		int sessions = SazExporter.Export(runDirectory, outputPath);
		Console.WriteLine($"Wrote {sessions} session(s) to {Path.GetFullPath(outputPath)}");
		return true;
	}
	catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) {
		Console.Error.WriteLine($"Could not write the .saz archive: {ex.Message}");
		return false;
	}
}

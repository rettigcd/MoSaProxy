using mos;

ProxyOptions options;
try { options = ProxyOptions.Parse(args); }
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
	return 1;
}

var sinks = new List<IExchangeLog> { new ConsoleExchangeLog() };
if (options.LogPath is not null) sinks.Add(new JsonLinesExchangeLog(options.LogPath));

await using var proxy = await ProxyHost.StartAsync(options, new CompositeExchangeLog(sinks.ToArray()));
Console.WriteLine($"HTTP proxy listening on 127.0.0.1:{proxy.Port}. Press Ctrl+C to stop.");
if (proxy.CaptureRunDirectory is not null) Console.WriteLine($"Capturing sessions to {proxy.CaptureRunDirectory}");
if (proxy.Authority is { } authority) {
	Console.WriteLine();
	Console.WriteLine("HTTPS interception is ON. Clients must trust the mos root certificate or they will show certificate errors.");
	Console.WriteLine($"  Root certificate: {authority.RootCertificatePath}");
	Console.WriteLine("  Keep the folder private: its private key can impersonate any website to a client that trusts it.");
	Console.WriteLine("  Trust it for a test Chrome only (no install; Chrome shows a warning bar):");
	Console.WriteLine($"    chrome.exe --user-data-dir=\"%TEMP%\\chrome-mos\" --proxy-server=\"http://127.0.0.1:{proxy.Port}\" --ignore-certificate-errors-spki-list={authority.SpkiSha256Base64}");
	Console.WriteLine("  Or install it for your Windows user (a confirmation dialog appears; remove it later with certutil -user -delstore Root \"mos Proxy Root CA (local test CA)\"):");
	Console.WriteLine($"    certutil -user -addstore Root \"{authority.RootCertificatePath}\"");
	Console.WriteLine();
}
await proxy.WaitForShutdownAsync();
return 0;

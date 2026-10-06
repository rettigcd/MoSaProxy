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
await proxy.WaitForShutdownAsync();
return 0;

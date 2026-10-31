using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace MoSaProxy;

/// <summary>Hosts the proxy on a Kestrel listener bound to loopback only.</summary>
public sealed class ProxyHost : IAsyncDisposable {
	readonly WebApplication _app;
	readonly ProxyHandler _handler;
	readonly CancellationTokenSource _stopping;   // shutdown has begun: no new connections, idle ones wind down
	readonly CancellationTokenSource _abort;      // the drain period is over: whatever is left is cut off
	readonly SwitchableSessionCapture _capture;
	readonly CaptureEvents _events;
	readonly CaptureRun? _captureRun;             // the temporary capture folder, deleted on dispose; null when the caller supplied the capture
	readonly object _stopLock = new();
	Task<ShutdownReport>? _stopTask;

	ProxyHost(WebApplication app, ProxyHandler handler, ProxyActivity activity, ResourceLimitMonitor limits, CancellationTokenSource stopping,
		CancellationTokenSource abort, int port, string? captureRunDirectory, CaptureRun? captureRun, SwitchableSessionCapture capture, CaptureEvents events, CertificateAuthority? authority) {
		_app = app;
		_handler = handler;
		Activity = activity;
		Limits = limits;
		_stopping = stopping;
		_abort = abort;
		Port = port;
		CaptureRunDirectory = captureRunDirectory;
		_captureRun = captureRun;
		_capture = capture;
		_events = events;
		Exchanges = captureRunDirectory is null ? null : new ExchangeStore(captureRunDirectory);
		Authority = authority;
	}

	/// <summary>The port actually bound (useful when the options asked for port 0).</summary>
	public int Port { get; }

	/// <summary>
	/// The folder this run's capture files go into, or null when the caller supplied the capture. Unless <see cref="ProxyOptions.CaptureDirectory"/>
	/// was set, it is a temporary folder that is deleted when the host is disposed, so read or export it before then.
	/// </summary>
	public string? CaptureRunDirectory { get; }

	/// <summary>
	/// Whether exchanges that begin now are captured. Switch it off to stop the capture files growing (traffic is still forwarded and
	/// logged), and on again to carry on numbering where it stopped. An exchange already running is not affected.
	/// </summary>
	public bool CaptureEnabled {
		get => _capture.Enabled;
		set {
			bool changed = _capture.Enabled != value;
			_capture.Enabled = value;
			if (changed) _events.RaiseCaptureEnabledChanged(value);
		}
	}

	/// <summary>The exchanges captured so far, to list and read; null when the caller supplied its own capture, which is not a folder this host knows.</summary>
	public ExchangeStore? Exchanges { get; }

	/// <summary>Announcements about captured exchanges and the capture switch, for a front end that shows them live.</summary>
	public CaptureEvents Events => _events;

	/// <summary>Why the temporary capture folder could not be deleted when the host was disposed, or null if it was (or there is none to delete).</summary>
	public string? CaptureCleanupError => _captureRun?.DeleteError;

	/// <summary>The certificate authority used for HTTPS interception, or null when interception is off.</summary>
	public CertificateAuthority? Authority { get; }

	/// <summary>How many requests, tunnels and WebSockets are in flight right now.</summary>
	public ProxyActivity Activity { get; }

	/// <summary>Reports when a limit is hit (disk full, out of memory, out of sockets...).</summary>
	public ResourceLimitMonitor Limits { get; }

	/// <param name="limits">Told when a limit is hit (disk full, out of memory, out of sockets...); the caller decides to stop. One is created if none is given.</param>
	public static async Task<ProxyHost> StartAsync(ProxyOptions options, IExchangeLog log, ISessionCapture? capture = null, ResourceLimitMonitor? limits = null) {
		limits ??= new ResourceLimitMonitor();
		string? runDirectory = null;
		CaptureRun? captureRun = null;
		if (capture is null) {
			FileSessionCapture files;
			if (options.CaptureDirectory is null) {
				captureRun = CaptureRun.Start(options.TemporaryCaptureRoot);
				files = FileSessionCapture.InFolder(captureRun.Directory, options.CaptureMaxBodyBytes, limits);
			}
			else files = new FileSessionCapture(options.CaptureDirectory, options.CaptureMaxBodyBytes, limits);
			runDirectory = files.RunDirectory;
			capture = files;
		}

		// Starting can still fail (the port is taken, the certificate authority cannot be loaded): the folder must not be left behind.
		var events = new CaptureEvents();
		var switchable = new SwitchableSessionCapture(new ObservedSessionCapture(capture, events), options.CaptureEnabled);
		// Exact wire bytes are only recorded when something keeps them.
		bool tapWire = capture is not NullSessionCapture;
		try { return await StartListeningAsync(options, log, switchable, events, tapWire, limits, runDirectory, captureRun); }
		catch { captureRun?.Dispose(); throw; }
	}

	static async Task<ProxyHost> StartListeningAsync(ProxyOptions options, IExchangeLog log, SwitchableSessionCapture capture, CaptureEvents events, bool tapWire, ResourceLimitMonitor limits,
		string? runDirectory, CaptureRun? captureRun) {
		var authority = options.Intercept ? CertificateAuthority.LoadOrCreate(options.CaDirectory) : null;
		var activity = new ProxyActivity();
		var stopping = new CancellationTokenSource();
		var abort = new CancellationTokenSource();
		// The taps cannot be added later, so they are always there when anything can keep what they record; an exchange that is not
		// captured still takes its wire bytes, which frees them.
		var wireTaps = tapWire ? new WireTaps(limits, log, capture) : null;
		var tunnels = new TunnelHandler(log, capture, options, stopping.Token, abort.Token, activity, limits, authority);
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		// The drain period is ours to decide (StopAsync), so the host's own shutdown time limit must not cut it short.
		builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromHours(1));
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = null;
			// The tunnel handler sits in front of Kestrel's HTTP handling and takes over CONNECT connections.
			kestrel.Listen(IPAddress.Loopback, options.Port, listen => {
				listen.Use(next => connection => tunnels.HandleAsync(connection, next));
				// After the tunnel handler, so that an intercepted tunnel's decrypted bytes are what is recorded.
				if (wireTaps is not null) listen.Use(next => connection => wireTaps.TapConnectionAsync(connection, next));
			});
		});

		var app = builder.Build();
		var handler = new ProxyHandler(log, capture, options, activity, limits, stopping.Token, abort.Token, wireTaps);
		// Pings are not generated by the proxy itself: the two WebSocket legs are independent and carry their own.
		app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = Timeout.InfiniteTimeSpan });
		app.Run(handler.HandleAsync);
		await app.StartAsync();

		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new ProxyHost(app, handler, activity, limits, stopping, abort, new Uri(address).Port, runDirectory, captureRun, capture, events, authority);
	}

	/// <summary>
	/// Completes when something asks the application to stop (Ctrl+C or a termination signal). It does not stop anything:
	/// the caller decides how long to drain and then calls <see cref="StopAsync"/>. (The framework's own WaitForShutdownAsync
	/// would stop the host straight away with its default time limit, which is exactly what a drain period must avoid.)
	/// </summary>
	public Task WaitForStopRequestAsync() {
		var requested = new TaskCompletionSource();
		_app.Lifetime.ApplicationStopping.Register(() => requested.TrySetResult());
		return requested.Task;
	}

	/// <summary>Asks the application to stop, as Ctrl+C does. It does not wait: <see cref="WaitForStopRequestAsync"/> then completes.</summary>
	public void RequestStop() => _app.Lifetime.StopApplication();

	/// <summary>
	/// Runs until something asks the proxy to stop or a limit is hit, then stops it and says how that went. This is the proxy's
	/// lifecycle policy, so that a front end only has to show it:
	/// <list type="bullet">
	/// <item>a stop request drains for up to <paramref name="drainTimeout"/> (or less, if <paramref name="skipDrain"/> is signalled);</item>
	/// <item>a limit stops the proxy at once with no drain, because it can no longer record what passes through it.</item>
	/// </list>
	/// </summary>
	/// <param name="onStopping">Called once the reason is known and before the stop starts, so a front end can tell the user (for example how much is still in flight, from <see cref="Activity"/>).</param>
	public async Task<StopResult> RunUntilStoppedAsync(TimeSpan drainTimeout, CancellationToken skipDrain = default, Action<StopReason>? onStopping = null) {
		var stopRequested = WaitForStopRequestAsync();
		var reason = await Task.WhenAny(stopRequested, Limits.WhenReached) == stopRequested ? StopReason.StopRequested : StopReason.LimitReached;

		onStopping?.Invoke(reason);
		var report = reason == StopReason.LimitReached
			? await StopAsync(TimeSpan.Zero)
			: await StopAsync(drainTimeout, skipDrain);

		return new StopResult(reason, report, Limits.Reached);
	}

	/// <summary>
	/// Shuts down in two stages. First the proxy stops accepting connections and starts winding down: running requests may finish,
	/// tunnels that are moving data may finish and quiet ones end, WebSockets are sent a close, idle connections are closed.
	/// After <paramref name="drainTimeout"/> (or as soon as <paramref name="skipDrain"/> is signalled) whatever is left is aborted.
	/// Safe to call more than once; later calls return the first call's report.
	/// </summary>
	public Task<ShutdownReport> StopAsync(TimeSpan drainTimeout, CancellationToken skipDrain = default) {
		lock (_stopLock) return _stopTask ??= StopCoreAsync(drainTimeout, skipDrain);
	}

	async Task<ShutdownReport> StopCoreAsync(TimeSpan drainTimeout, CancellationToken skipDrain) {
		_stopping.Cancel();

		// Kestrel stops listening, closes idle connections, and waits for the others. It gives up (aborting them) only when its
		// token fires, and that is ours to fire, so we can count what was still running at that moment before it is cut off.
		using var abortKestrel = new CancellationTokenSource();
		var kestrelStopped = _app.StopAsync(abortKestrel.Token);
		var drainOver = DelayUnlessSkippedAsync(drainTimeout, skipDrain);

		if (await Task.WhenAny(kestrelStopped, drainOver) != kestrelStopped) {
			var stillRunning = new ShutdownReport(Activity.Exchanges, Activity.Tunnels, Activity.WebSockets);
			abortKestrel.Cancel();
			await FinishQuietlyAsync(kestrelStopped);
			_abort.Cancel();
			await Activity.WaitForIdleAsync(TimeSpan.FromSeconds(2));
			return stillRunning;
		}

		await FinishQuietlyAsync(kestrelStopped);
		var report = new ShutdownReport(Activity.Exchanges, Activity.Tunnels, Activity.WebSockets);
		_abort.Cancel();
		// Aborted requests still log and close their capture files as they unwind; give them a moment to do that.
		await Activity.WaitForIdleAsync(TimeSpan.FromSeconds(2));
		return report;
	}

	/// <summary>Completes after <paramref name="delay"/>, or as soon as <paramref name="skip"/> is signalled (without throwing).</summary>
	static async Task DelayUnlessSkippedAsync(TimeSpan delay, CancellationToken skip) {
		try { await Task.Delay(delay, skip); }
		catch (OperationCanceledException) { }
	}

	/// <summary>Waits for Kestrel's stop to end; being aborted makes it throw, which is expected and not an error here.</summary>
	static async Task FinishQuietlyAsync(Task stop) {
		try { await stop; }
		catch (Exception ex) when (ex is OperationCanceledException or AggregateException) { }
	}

	public async ValueTask DisposeAsync() {
		await StopAsync(TimeSpan.Zero);   // already stopped normally: this returns at once
		await _app.DisposeAsync();
		_handler.Dispose();
		_stopping.Dispose();
		_abort.Dispose();
		_captureRun?.Dispose();   // last: the capture files are closed by now
	}
}

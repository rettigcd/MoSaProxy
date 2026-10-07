namespace MoSaProxy;

/// <summary>Writes each run's exchanges into a new sub-folder of <c>baseDirectory</c>, numbered from 1.</summary>
public sealed class FileSessionCapture : ISessionCapture {
	readonly long? _maxBodyBytes;
	int _last;

	readonly ResourceLimitMonitor? _limits;

	public FileSessionCapture(string baseDirectory, long? maxBodyBytes = null, ResourceLimitMonitor? limits = null) {
		_maxBodyBytes = maxBodyBytes;
		_limits = limits;
		Directory.CreateDirectory(baseDirectory);
		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string run = Path.Combine(baseDirectory, stamp);
		for (int i = 2; Directory.Exists(run); i++) run = Path.Combine(baseDirectory, $"{stamp}-{i}");
		Directory.CreateDirectory(run);
		RunDirectory = run;
	}

	public string RunDirectory { get; }

	public IExchangeCapture BeginExchange() {
		int number = Interlocked.Increment(ref _last);
		return new FileExchangeCapture(RunDirectory, number, _maxBodyBytes, _limits);
	}
}

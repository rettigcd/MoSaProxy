using System.Security.Cryptography;
using System.Text;

namespace MoSaProxy;

/// <summary>
/// One run's temporary capture folder. It is created inside <see cref="DefaultRoot"/> (<c>%TEMP%\MoSaProxyCaptures</c>) and deleted
/// when the run is disposed: nothing of a run's capture survives the program.
/// Every folder directly inside the root is a run folder (there are no marker files naming them). A run that was killed cannot
/// delete its folder, so <see cref="Start"/> deletes the folders of runs that are gone. To tell those from the folder of a MoSaProxy that
/// is running right now, each run holds a file in its folder open with exclusive access for as long as it lives; a folder whose
/// file can be opened has no owner left.
/// </summary>
public sealed class CaptureRun : IDisposable {
	const string LockFileName = ".lock";

	readonly FileStream _lock;
	bool _disposed;

	CaptureRun(string directory, FileStream lockFile) {
		Directory = directory;
		_lock = lockFile;
	}

	/// <summary>The folder the system uses for temporary files, plus <c>MoSaProxyCaptures</c>.</summary>
	public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "MoSaProxyCaptures");

	/// <summary>This run's folder, a direct child of the root.</summary>
	public string Directory { get; }

	/// <summary>Why the folder could not be deleted by <see cref="Dispose"/>, or null if it was (or has not been tried yet).</summary>
	public string? DeleteError { get; private set; }

	/// <summary>
	/// Deletes the folders of runs that are over, then creates and locks a folder for this run. The sweep and the creation happen
	/// under a lock shared by every MoSaProxy on the machine, so one starting up cannot delete another's folder before it is locked.
	/// </summary>
	public static CaptureRun Start(string root) {
		root = Path.GetFullPath(root);
		using var gate = new Mutex(false, "MoSaProxyCaptures-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..32]);
		try { gate.WaitOne(); }
		catch (AbandonedMutexException) { }   // the previous holder died; the lock is ours and nothing it half-did matters here
		try {
			System.IO.Directory.CreateDirectory(root);
			foreach (string folder in System.IO.Directory.EnumerateDirectories(root)) DeleteIfAbandoned(folder);

			// The name is never reused: a run that was disposed may still be retrying its delete by path, and a new run that took the
			// same name would lose its files to that retry.
			string run = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
			System.IO.Directory.CreateDirectory(run);
			var lockFile = new FileStream(Path.Combine(run, LockFileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
			return new CaptureRun(run, lockFile);
		}
		finally { gate.ReleaseMutex(); }
	}

	/// <summary>Deletes the folder if no run owns it. A folder that is in use, or that cannot be deleted, is left alone.</summary>
	static void DeleteIfAbandoned(string folder) {
		string lockPath = Path.Combine(folder, LockFileName);
		FileStream? owned = null;
		try {
			// Delete sharing, so that our own handle does not stop the folder from being removed; a live run holds the file with
			// no sharing, which makes this open fail.
			if (File.Exists(lockPath)) owned = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete, 1, FileOptions.DeleteOnClose);
			DeleteContents(folder, except: lockPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
		finally { owned?.Dispose(); }

		try { System.IO.Directory.Delete(folder); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
	}

	static void DeleteContents(string folder, string except) {
		foreach (string file in System.IO.Directory.EnumerateFiles(folder))
			if (!string.Equals(file, except, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
		foreach (string sub in System.IO.Directory.EnumerateDirectories(folder)) System.IO.Directory.Delete(sub, recursive: true);
	}

	/// <summary>Deletes the run's folder. If that fails (a viewer holds a file open) the reason is in <see cref="DeleteError"/>; the next run's start-up sweep tries again.</summary>
	public void Dispose() {
		if (_disposed) return;
		_disposed = true;
		_lock.Dispose();
		try { DeleteWithRetry(Directory); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DeleteError = ex.Message; }
	}

	/// <summary>A file the proxy has only just closed can stay locked for a moment (antivirus, the search indexer), so a failure is tried a few times.</summary>
	static void DeleteWithRetry(string folder) {
		for (int attempt = 1; ; attempt++) {
			try { System.IO.Directory.Delete(folder, recursive: true); return; }
			catch (DirectoryNotFoundException) { return; }
			catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(50 * attempt); }
		}
	}
}

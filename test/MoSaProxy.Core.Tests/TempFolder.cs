namespace MoSaProxy.Tests;

/// <summary>A temporary folder that is deleted when disposed.</summary>
public sealed class TempFolder : IDisposable {
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mosaproxy-test-{Guid.NewGuid():N}");

	public TempFolder() => Directory.CreateDirectory(Path);

	/// <summary>Waits for a file to appear (capture metadata is written just after the client gets its response).</summary>
	public static async Task<string> WaitForFileAsync(string path) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && !CanBeRead(path)) await Task.Delay(10);
		return path;
	}

	/// <summary>True once the file exists and can be opened for reading the way a reader would (it can be held for a moment right after it appears).</summary>
	static bool CanBeRead(string path) {
		try { using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
		catch (IOException) { return false; }
	}

	public void Dispose() {
		try { Directory.Delete(Path, recursive: true); } catch { }
	}
}

namespace MoSaProxy.Tests;

/// <summary>A temporary folder that is deleted when disposed.</summary>
public sealed class TempFolder : IDisposable {
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mosaproxy-test-{Guid.NewGuid():N}");

	public TempFolder() => Directory.CreateDirectory(Path);

	/// <summary>Waits for a file to appear (capture metadata is written just after the client gets its response).</summary>
	public static async Task<string> WaitForFileAsync(string path) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!File.Exists(path) && DateTime.UtcNow < deadline) await Task.Delay(10);
		return path;
	}

	public void Dispose() {
		try { Directory.Delete(Path, recursive: true); } catch { }
	}
}

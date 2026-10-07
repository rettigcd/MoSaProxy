using System.Text.Json;

namespace MoSaProxy;

/// <summary>
/// Appends one JSON object per line to a file. With <c>maxBytes</c> set, the file is rotated before a write would take it past that size:
/// "traffic.jsonl" becomes "traffic.1.jsonl", the old "traffic.1.jsonl" becomes "traffic.2.jsonl", and so on up to <c>keep</c> rotated files;
/// the oldest one beyond that is deleted. A single line larger than the limit is still written (into a fresh file), never dropped.
/// </summary>
public sealed class JsonLinesExchangeLog : IExchangeLog {
	static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
	readonly string _path;
	readonly long _maxBytes;
	readonly int _keep;
	readonly object _lock = new();
	long _size;           // bytes in the current file
	long _rotateAfter;    // after a failed rotation, do not try again (or complain again) until the file has grown this big

	/// <param name="maxBytes">Rotate before a write would pass this size; 0 means never rotate.</param>
	/// <param name="keep">How many rotated files to keep; 0 means the old file is discarded when rotating.</param>
	public JsonLinesExchangeLog(string path, long maxBytes = 0, int keep = 5) {
		_path = Path.GetFullPath(path);
		_maxBytes = maxBytes;
		_keep = keep;
		var directory = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		_size = File.Exists(_path) ? new FileInfo(_path).Length : 0;
	}

	public void Write(ExchangeRecord record) {
		var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
		lock (_lock) {
			if (_maxBytes > 0 && _size > 0 && _size + bytes.Length > _maxBytes && _size >= _rotateAfter) Rotate();
			using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
				stream.Write(bytes);
			_size += bytes.Length;
		}
	}

	void Rotate() {
		try {
			string directory = Path.GetDirectoryName(_path)!;
			string name = Path.GetFileNameWithoutExtension(_path);
			string extension = Path.GetExtension(_path);
			string Rotated(int number) => Path.Combine(directory, $"{name}.{number}{extension}");

			if (_keep == 0) {
				File.Delete(_path);
			}
			else {
				File.Delete(Rotated(_keep));                       // the oldest one falls off the end
				for (int number = _keep - 1; number >= 1; number--)
					if (File.Exists(Rotated(number))) File.Move(Rotated(number), Rotated(number + 1));
				File.Move(_path, Rotated(1));
			}
			_size = 0;
			_rotateAfter = 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// For example a viewer has a rotated file open. Losing log lines would be worse than a big file, so keep appending.
			Console.Error.WriteLine($"Log rotation failed ({ex.Message}); continuing to append to {_path}.");
			_rotateAfter = _size + _maxBytes;
		}
	}
}

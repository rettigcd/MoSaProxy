using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MoSaProxy;

/// <summary>
/// Reads the exchanges captured into a run folder: a list of summaries, one exchange's details, and its request and response bytes.
/// An exchange appears once its metadata file (written last, by rename) exists, so one that is still running is not listed and
/// nothing is ever half read. It reads files only; it never changes them.
/// </summary>
public sealed class ExchangeStore(string directory) {
	static readonly Regex MetadataFile = new(@"^(\d+)_m\.xml$", RegexOptions.Compiled);
	const int MaxHeadBytes = 256 * 1024;

	readonly ConcurrentDictionary<int, ExchangeSummary> _summaries = new();   // an exchange's metadata never changes once written

	/// <summary>The captured exchanges that match <paramref name="query"/>, oldest first.</summary>
	public IReadOnlyList<ExchangeSummary> List(ExchangeQuery? query = null) {
		query ??= new ExchangeQuery();
		var result = new List<ExchangeSummary>();
		foreach (int id in Ids()) {
			if (query.After is { } after && id <= after) continue;
			if (Summary(id) is not { } summary || !Matches(summary, query)) continue;
			result.Add(summary);
			if (result.Count >= query.Limit) break;
		}
		return result;
	}

	/// <summary>How many exchanges are captured and complete.</summary>
	public int Count => Ids().Count();

	/// <summary>One exchange's summary, or null if there is no completed exchange with that number.</summary>
	public ExchangeSummary? Summary(int id) {
		if (_summaries.TryGetValue(id, out var cached)) return cached;
		if (ReadMetadata(id) is not { } root) return null;
		return _summaries[id] = ToSummary(id, root);
	}

	/// <summary>One exchange's details, or null if there is no completed exchange with that number.</summary>
	public ExchangeDetail? Detail(int id) {
		if (ReadMetadata(id) is not { } root) return null;
		string Text(string name) => root.Element(name)?.Value ?? "";
		bool Flag(string name) => Text(name) == "true";
		int? OptionalInt(string name) => int.TryParse(Text(name), out int value) ? value : null;
		var times = new Dictionary<string, DateTimeOffset>();
		foreach (var attribute in root.Element("Times")?.Attributes() ?? [])
			if (DateTimeOffset.TryParse(attribute.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) times[attribute.Name.LocalName] = at;

		return new ExchangeDetail(
			Summary(id)!, Text("Client"), OptionalInt("ClientPort") ?? 0, Text("ClientTlsVersion") is { Length: > 0 } tls ? tls : null,
			Flag("ClientConnectionReused"), Flag("OriginConnectionReused"), OptionalInt("OriginConnectionId"),
			OptionalInt("ClientMessages"), OptionalInt("OriginMessages"),
			Text("RequestSource"), Text("ResponseSource"), Flag("RequestBodyTruncated"), Flag("ResponseBodyTruncated"),
			times, ReadHead($"{id}_c.txt"), ReadHead($"{id}_s.txt"), File.Exists(Path.Combine(directory, $"{id}_w.txt")));
	}

	/// <summary>The request exactly as captured (<c>N_c.txt</c>): the head and body, or for a CONNECT whatever follows it. Null if there is no such exchange. The caller disposes the stream.</summary>
	public Stream? OpenRequest(int id) => Open(id, $"{id}_c.txt");

	/// <summary>The response exactly as captured (<c>N_s.txt</c>). Null if there is no such exchange. The caller disposes the stream.</summary>
	public Stream? OpenResponse(int id) => Open(id, $"{id}_s.txt");

	/// <summary>The readable log of a WebSocket exchange's messages (<c>N_w.txt</c>), or null if it has none. The caller disposes the stream.</summary>
	public Stream? OpenWebSocketLog(int id) => Open(id, $"{id}_w.txt");

	Stream? Open(int id, string name) {
		if (Summary(id) is null) return null;
		try { return new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
	}

	IEnumerable<int> Ids() {
		string[] files;
		try { files = Directory.GetFiles(directory, "*_m.xml"); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
		return files.Select(path => MetadataFile.Match(Path.GetFileName(path))).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Order();
	}

	XElement? ReadMetadata(int id) {
		try { return XDocument.Load(Path.Combine(directory, $"{id}_m.xml")).Root; }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return null; }
	}

	static ExchangeSummary ToSummary(int id, XElement root) {
		string Text(string name) => root.Element(name)?.Value ?? "";
		long Number(string name) => long.TryParse(Text(name), out long value) ? value : 0;
		string url = Text("Url");
		string path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;
		var timestamp = DateTimeOffset.TryParse(Text("Timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : default;
		double duration = double.TryParse(Text("DurationMs"), System.Globalization.CultureInfo.InvariantCulture, out double ms) ? ms : 0;
		return new ExchangeSummary(id, timestamp, Text("Method"), Text("Scheme"), Text("Host"), (int)Number("Port"), path, url,
			int.TryParse(Text("Status"), out int status) ? status : null, Text("ClientHttpVersion"),
			Text("OriginHttpVersion") is { Length: > 0 } origin ? origin : null, Text("Mode"), Number("RequestBytes"), Number("ResponseBytes"), duration,
			Text("Error") is { Length: > 0 } error ? error : null);
	}

	static bool Matches(ExchangeSummary e, ExchangeQuery q) =>
		(q.Host is null || e.Host.Contains(q.Host, StringComparison.OrdinalIgnoreCase)) &&
		(q.Method is null || e.Method.Equals(q.Method, StringComparison.OrdinalIgnoreCase)) &&
		(q.Status is null || e.Status == q.Status) &&
		(q.Mode is null || e.Mode.Equals(q.Mode, StringComparison.OrdinalIgnoreCase)) &&
		(q.Text is null || e.Url.Contains(q.Text, StringComparison.OrdinalIgnoreCase));

	/// <summary>Reads a captured file's first line and headers (up to the first blank line). Null if the file is missing or holds no complete head.</summary>
	HttpHead? ReadHead(string name) {
		byte[] buffer;
		try {
			using var file = new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			buffer = new byte[(int)Math.Min(file.Length, MaxHeadBytes)];
			file.ReadExactly(buffer);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

		int end = buffer.AsSpan().IndexOf("\r\n\r\n"u8);
		if (end < 0) return null;
		// Header lines are Latin-1: each byte is one character, so nothing is lost or misread.
		var lines = Encoding.Latin1.GetString(buffer, 0, end).Split("\r\n");
		var headers = new List<KeyValuePair<string, string>>();
		foreach (string line in lines.Skip(1)) {
			int colon = line.IndexOf(':');
			if (colon > 0) headers.Add(new(line[..colon], line[(colon + 1)..].Trim()));
		}
		return new HttpHead(lines[0], headers);
	}
}

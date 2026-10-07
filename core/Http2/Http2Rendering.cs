using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace MoSaProxy;

/// <summary>
/// Writes an HTTP/2 request or response as HTTP/1.1-style text: a first line, the header fields in the order they were sent, a blank
/// line and the body. The pseudo-headers become the first line (and a Host header for <c>:authority</c>). Names are lower case,
/// as HTTP/2 sends them. Trailers are not written. Text is Latin-1, so every byte survives.
/// </summary>
static class Http2Rendering {
	public static byte[]? Request(Http2Stream stream) {
		if (stream.HeaderBlocks.Count == 0) return null;
		var headers = stream.HeaderBlocks[0];
		string method = Pseudo(headers, ":method"), path = Pseudo(headers, ":path"), authority = Pseudo(headers, ":authority");
		var text = new StringBuilder($"{method} {path} HTTP/2\r\n");
		if (authority.Length > 0) text.Append("Host: ").Append(authority).Append("\r\n");
		return Finish(text, headers, stream.Body);
	}

	public static byte[]? Response(Http2Stream stream) {
		// 1xx interim responses come first and are skipped; the final head is the first block that has a status of 200 or more.
		var headers = stream.HeaderBlocks.FirstOrDefault(block => int.TryParse(Pseudo(block, ":status"), out int status) && status >= 200);
		if (headers is null) return null;
		string status = Pseudo(headers, ":status");
		string reason = int.TryParse(status, out int code) ? ReasonPhrases.GetReasonPhrase(code) : "";
		return Finish(new StringBuilder($"HTTP/2 {status} {reason}\r\n"), headers, stream.Body);
	}

	static byte[] Finish(StringBuilder text, List<KeyValuePair<string, string>> headers, MemoryStream body) {
		foreach (var (name, value) in headers)
			if (!name.StartsWith(':')) text.Append(name).Append(": ").Append(value).Append("\r\n");
		text.Append("\r\n");
		var head = Encoding.Latin1.GetBytes(text.ToString());
		var result = new byte[head.Length + body.Length];
		head.CopyTo(result, 0);
		body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(result.AsSpan(head.Length));
		return result;
	}

	static string Pseudo(List<KeyValuePair<string, string>> headers, string name) => headers.FirstOrDefault(h => h.Key == name).Value ?? "";
}

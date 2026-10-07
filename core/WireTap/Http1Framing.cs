using System.Text;

namespace MoSaProxy;

/// <summary>
/// Finds where HTTP/1.1 messages begin and end in a byte stream, using only the framing rules (the head, then a body of
/// <c>Content-Length</c> bytes, or chunked, or none). Nothing is interpreted beyond that, and nothing is copied or changed.
/// </summary>
public static class Http1Framing {
	/// <summary>A message found in a byte stream: where it starts, how long it is, and its first line.</summary>
	/// <param name="Complete">False when the stream ends before the message does; <paramref name="Length"/> then runs to the end of the bytes.</param>
	public readonly record struct Message(int Start, int Length, string StartLine, bool Complete);

	/// <summary>Finds the request starting at <paramref name="start"/>, or null when not even its head is complete yet or it cannot be framed.</summary>
	public static Message? FindRequest(ReadOnlySpan<byte> bytes, int start) => Find(bytes, start, isResponse: false, requestMethod: null);

	/// <summary>
	/// Finds the final response starting at <paramref name="start"/>, skipping any 1xx interim responses before it.
	/// The request's method is needed because a response to HEAD has no body.
	/// </summary>
	public static Message? FindResponse(ReadOnlySpan<byte> bytes, int start, string requestMethod) {
		int pos = start;
		while (true) {
			var found = Find(bytes, pos, isResponse: true, requestMethod);
			if (found is not { } m) return null;
			if (!m.Complete || !IsInterim(m.StartLine)) return new Message(start, m.Start + m.Length - start, m.StartLine, m.Complete);
			pos = m.Start + m.Length;
		}
	}

	static bool IsInterim(string statusLine) => TryStatus(statusLine, out int status) && status is >= 100 and < 200 && status != 101;

	static bool TryStatus(string statusLine, out int status) {
		status = 0;
		var parts = statusLine.Split(' ', 3);
		return parts.Length >= 2 && int.TryParse(parts[1], out status);
	}

	static Message? Find(ReadOnlySpan<byte> bytes, int start, bool isResponse, string? requestMethod) {
		int headEnd = IndexOfBlankLine(bytes, start);
		if (headEnd < 0) return null;
		var lines = Encoding.Latin1.GetString(bytes[start..headEnd]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length == 0) return null;

		bool noBody = false;
		if (isResponse) {
			if (!TryStatus(lines[0], out int status)) return null;
			noBody = string.Equals(requestMethod, "HEAD", StringComparison.OrdinalIgnoreCase) || status is >= 100 and < 200 or 204 or 304;
		}

		int end = noBody ? headEnd : BodyEnd(bytes, headEnd, lines, untilCloseIfNoLength: isResponse);
		if (end < 0) return new Message(start, bytes.Length - start, lines[0], Complete: false);
		return new Message(start, end - start, lines[0], Complete: true);
	}

	static int IndexOfBlankLine(ReadOnlySpan<byte> b, int from) {
		var rel = b[from..].IndexOf("\r\n\r\n"u8);
		return rel < 0 ? -1 : from + rel + 4;
	}

	/// <summary>The end of the body, or -1 when the bytes stop before it. Without a length, a response runs to the end of the stream.</summary>
	static int BodyEnd(ReadOnlySpan<byte> b, int bodyStart, string[] lines, bool untilCloseIfNoLength) {
		string? Header(string name) {
			foreach (var line in lines.Skip(1)) {
				int colon = line.IndexOf(':');
				if (colon > 0 && line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return line[(colon + 1)..].Trim();
			}
			return null;
		}

		if (Header("Transfer-Encoding") is { } coding && coding.Contains("chunked", StringComparison.OrdinalIgnoreCase)) {
			int pos = bodyStart;
			while (true) {
				int eol = Line(b, pos);
				if (eol < 0) return -1;
				string size = Encoding.Latin1.GetString(b[pos..eol]).Split(';')[0].Trim();
				if (!int.TryParse(size, System.Globalization.NumberStyles.HexNumber, null, out int chunk)) return -1;
				pos = eol + 2;
				if (chunk == 0) {
					while (true) {          // trailer lines, then the blank line that ends the message
						int e2 = Line(b, pos);
						if (e2 < 0) return -1;
						bool blank = e2 == pos;
						pos = e2 + 2;
						if (blank) return pos;
					}
				}
				pos += chunk + 2;
				if (pos > b.Length) return -1;
			}
		}

		if (Header("Content-Length") is { } length && long.TryParse(length, out long n) && n >= 0) {
			long end = bodyStart + n;
			return end <= b.Length ? (int)end : -1;
		}
		return untilCloseIfNoLength ? -1 : bodyStart;
	}

	static int Line(ReadOnlySpan<byte> b, int from) {
		var rel = b[from..].IndexOf("\r\n"u8);
		return rel < 0 ? -1 : from + rel;
	}
}

using System.IO.Compression;
using System.Text;

namespace MoSaProxy;

/// <summary>A message body taken out of captured bytes.</summary>
/// <param name="Bytes">The body: as captured, or with the chunk framing and content coding removed when that was asked for and possible.</param>
/// <param name="ContentType">The message's <c>Content-Type</c> header, if it has one.</param>
/// <param name="Decoded">True when decoding was asked for and every coding the message declared was removed.</param>
public sealed record BodyContent(byte[] Bytes, string? ContentType, bool Decoded);

/// <summary>Splits a captured HTTP message (head, blank line, body) and, on request, undoes its chunked framing and gzip, deflate or brotli content coding.</summary>
public static class HttpBody {

	/// <summary>The body of the captured message, or null when the bytes hold no complete head.</summary>
	public static BodyContent? Extract(byte[] message, bool decode) {
		int end = message.AsSpan().IndexOf("\r\n\r\n"u8);
		if (end < 0) return null;
		var headers = new List<KeyValuePair<string, string>>();
		foreach (string line in Encoding.Latin1.GetString(message, 0, end).Split("\r\n").Skip(1)) {
			int colon = line.IndexOf(':');
			if (colon > 0) headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
		}
		string? Header(string name) => headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

		var body = message.AsSpan(end + 4).ToArray();
		string? contentType = Header("Content-Type");
		if (!decode) return new BodyContent(body, contentType, false);

		if (Header("Transfer-Encoding") is { } transfer && transfer.Contains("chunked", StringComparison.OrdinalIgnoreCase)) body = Dechunk(body);

		var codings = (Header("Content-Encoding") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(c => !c.Equals("identity", StringComparison.OrdinalIgnoreCase)).ToList();
		// Codings were applied in the order listed, so they come off in reverse.
		for (int i = codings.Count - 1; i >= 0; i--) {
			var decoded = Decompress(codings[i], body);
			if (decoded is null) return new BodyContent(body, contentType, false);
			body = decoded;
		}
		return new BodyContent(body, contentType, true);
	}

	/// <summary>Joins the chunks of a chunked body. A body that stops part way gives what arrived.</summary>
	static byte[] Dechunk(byte[] body) {
		var result = new MemoryStream();
		int pos = 0;
		while (pos < body.Length) {
			int eol = body.AsSpan(pos).IndexOf("\r\n"u8);
			if (eol < 0) break;
			string size = Encoding.Latin1.GetString(body, pos, eol).Split(';')[0].Trim();
			if (!int.TryParse(size, System.Globalization.NumberStyles.HexNumber, null, out int chunk) || chunk == 0) break;
			pos += eol + 2;
			int available = Math.Min(chunk, body.Length - pos);
			result.Write(body, pos, available);
			pos += chunk + 2;
		}
		return result.ToArray();
	}

	static byte[]? Decompress(string coding, byte[] data) {
		try {
			using var input = new MemoryStream(data);
			Stream? stream = coding.ToLowerInvariant() switch {
				"gzip" or "x-gzip" => new GZipStream(input, CompressionMode.Decompress),
				"deflate" => new ZLibStream(input, CompressionMode.Decompress),
				"br" => new BrotliStream(input, CompressionMode.Decompress),
				_ => null,
			};
			if (stream is null) return null;
			using (stream) {
				var output = new MemoryStream();
				stream.CopyTo(output);
				return output.ToArray();
			}
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException) { return null; }
	}
}

using System.IO.Compression;
using System.Text;
using Shouldly;

namespace MoSaProxy.Tests;

public class HttpBody_Tests {

	static byte[] Message(string head, byte[] body) => [.. Encoding.Latin1.GetBytes(head + "\r\n\r\n"), .. body];

	static byte[] Gzip(string text) {
		var output = new MemoryStream();
		using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(text));
		return output.ToArray();
	}

	[Fact]
	public void Extract_WithoutDecode_ReturnsTheBodyAsCaptured() {

		// Given: a chunked response
		var message = Message("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Type: text/plain", "5\r\nhello\r\n0\r\n\r\n"u8.ToArray());

		// When: the body is taken without decoding
		var body = HttpBody.Extract(message, decode: false)!;

		// Then: the chunk framing is still there and the content type is reported
		Encoding.ASCII.GetString(body.Bytes).ShouldBe("5\r\nhello\r\n0\r\n\r\n");
		body.ContentType.ShouldBe("text/plain");
		body.Decoded.ShouldBeFalse();
	}

	[Fact]
	public void Extract_ChunkedAndGzipped_WithDecode_ReturnsThePlainText() {

		// Given: a gzipped body sent in two chunks
		var zipped = Gzip("hello, world");
		var chunked = new MemoryStream();
		foreach (var part in new[] { zipped[..5], zipped[5..] }) {
			chunked.Write(Encoding.ASCII.GetBytes($"{part.Length:x}\r\n"));
			chunked.Write(part);
			chunked.Write("\r\n"u8);
		}
		chunked.Write("0\r\n\r\n"u8);
		var message = Message("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Encoding: gzip", chunked.ToArray());

		// When: the body is taken with decoding
		var body = HttpBody.Extract(message, decode: true)!;

		// Then: it is the original text, and it is marked decoded
		Encoding.UTF8.GetString(body.Bytes).ShouldBe("hello, world");
		body.Decoded.ShouldBeTrue();
	}

	[Fact]
	public void Extract_WithAnUnknownCoding_WithDecode_ReturnsTheBodyAndSaysItIsNotDecoded() {

		// Given: a body in a coding this code does not know
		var message = Message("HTTP/1.1 200 OK\r\nContent-Encoding: zstd", "abc"u8.ToArray());

		// When: decoding is asked for
		var body = HttpBody.Extract(message, decode: true)!;

		// Then: the bytes come back as captured, marked not decoded
		Encoding.ASCII.GetString(body.Bytes).ShouldBe("abc");
		body.Decoded.ShouldBeFalse();
	}

	[Fact]
	public void Extract_WithADamagedGzipBody_WithDecode_ReturnsTheBodyAndSaysItIsNotDecoded() {

		// Given: a body that claims to be gzip but is not
		var message = Message("HTTP/1.1 200 OK\r\nContent-Encoding: gzip", "not gzip"u8.ToArray());

		// When: decoding is asked for
		var body = HttpBody.Extract(message, decode: true)!;

		// Then: it does not throw; the bytes come back as captured, marked not decoded
		Encoding.ASCII.GetString(body.Bytes).ShouldBe("not gzip");
		body.Decoded.ShouldBeFalse();
	}

	[Fact]
	public void Extract_WhenAChunkedBodyStopsPartWay_WithDecode_ReturnsWhatArrived() {

		// Given: a chunked body cut off in the middle of its second chunk
		var message = Message("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked", "3\r\nabc\r\n5\r\nde"u8.ToArray());

		// When: decoding is asked for
		var body = HttpBody.Extract(message, decode: true)!;

		// Then: both the whole chunk and the partial one are kept
		Encoding.ASCII.GetString(body.Bytes).ShouldBe("abcde");
	}

	[Fact]
	public void Extract_WithNoCompleteHead_ReturnsNull() {

		// Given: bytes that stop before the blank line
		var message = "HTTP/1.1 200 OK\r\nContent-Type: text/plain"u8.ToArray();

		// When: the body is taken
		var body = HttpBody.Extract(message, decode: false);

		// Then: there is none to give
		body.ShouldBeNull();
	}
}

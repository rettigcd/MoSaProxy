using System.Text;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>Uses the worked examples in RFC 7541, Appendix C.</summary>
public class HpackDecoder_Tests {

	static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

	static string[] Names(List<KeyValuePair<string, string>> headers) => headers.Select(h => $"{h.Key}: {h.Value}").ToArray();

	[Fact]
	public void Decode_RequestWithoutHuffman_ReturnsTheFields() {

		// Given: the first request of RFC 7541 C.3 (literal strings, no Huffman coding)
		var decoder = new HpackDecoder();

		// When: its header block is decoded
		var headers = decoder.Decode(Hex("8286 8441 0f77 7777 2e65 7861 6d70 6c65 2e63 6f6d"));

		// Then: the four request fields come back in order
		Names(headers).ShouldBe([":method: GET", ":scheme: http", ":path: /", ":authority: www.example.com"]);
	}

	[Fact]
	public void Decode_ThreeRequestsWithHuffman_UseTheDynamicTableAcrossBlocks() {

		// Given: one decoder, which keeps the dynamic table between blocks, and the three requests of RFC 7541 C.4 (Huffman coded)
		var decoder = new HpackDecoder();

		// When: the three header blocks are decoded in order
		var first = decoder.Decode(Hex("8286 8441 8cf1 e3c2 e5f2 3a6b a0ab 90f4 ff"));
		var second = decoder.Decode(Hex("8286 84be 5886 a8eb 1064 9cbf"));
		var third = decoder.Decode(Hex("8287 85bf 4088 25a8 49e9 5ba9 7d7f 8925 a849 e95b b8e8 b4bf"));

		// Then: the second reuses the :authority from the dynamic table, and the third reuses it and adds a custom field
		Names(first).ShouldBe([":method: GET", ":scheme: http", ":path: /", ":authority: www.example.com"]);
		Names(second).ShouldBe([":method: GET", ":scheme: http", ":path: /", ":authority: www.example.com", "cache-control: no-cache"]);
		Names(third).ShouldBe([":method: GET", ":scheme: https", ":path: /index.html", ":authority: www.example.com", "custom-key: custom-value"]);
	}

	[Fact]
	public void Decode_ResponseWithHuffman_ReturnsTheFields() {

		// Given: the first response of RFC 7541 C.6 (Huffman coded)
		var decoder = new HpackDecoder();

		// When: its header block is decoded
		var headers = decoder.Decode(Hex(
			"4882 6402 5885 aec3 771a 4b61 96d0 7abe 9410 54d4 44a8 2005 9504 0b81 66e0 82a6 2d1b ff6e 919d 29ad 1718 63c7 8f0b 97c8 e9ae 82ae 43d3"));

		// Then: the status, cache-control, date and location come back
		Names(headers).ShouldBe([":status: 302", "cache-control: private", "date: Mon, 21 Oct 2013 20:13:21 GMT", "location: https://www.example.com"]);
	}

	[Fact]
	public void Decode_IndexPastTheTable_Throws() {

		// Given: a fresh decoder (empty dynamic table)
		var decoder = new HpackDecoder();

		// When / Then: a block that points at dynamic entry 62 is rejected
		Should.Throw<InvalidDataException>(() => decoder.Decode(Hex("be")));
	}

	[Fact]
	public void HuffmanDecode_WrongPadding_Throws() {

		// Given: a Huffman string whose last bits are zeros instead of the required ones
		var bytes = Hex("f1e3 c2e5 f23a 6ba0 ab90 f400");

		// When / Then: decoding fails
		Should.Throw<InvalidDataException>(() => HpackHuffman.Decode(bytes));
	}
}

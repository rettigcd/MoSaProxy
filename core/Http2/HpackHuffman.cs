namespace MoSaProxy;

/// <summary>
/// The HPACK Huffman code (RFC 7541, Appendix B) and a decoder for it. Each entry is the code, left-aligned in 32 bits, and its length in bits;
/// the 257th entry is the end-of-string symbol, which never appears inside a string.
/// </summary>
static class HpackHuffman {
	static readonly (uint Code, int Length)[] Table = [
		(0xffc00000u, 13), (0xffffb000u, 23), (0xfffffe20u, 28), (0xfffffe30u, 28),
		(0xfffffe40u, 28), (0xfffffe50u, 28), (0xfffffe60u, 28), (0xfffffe70u, 28),
		(0xfffffe80u, 28), (0xffffea00u, 24), (0xfffffff0u, 30), (0xfffffe90u, 28),
		(0xfffffea0u, 28), (0xfffffff4u, 30), (0xfffffeb0u, 28), (0xfffffec0u, 28),
		(0xfffffed0u, 28), (0xfffffee0u, 28), (0xfffffef0u, 28), (0xffffff00u, 28),
		(0xffffff10u, 28), (0xffffff20u, 28), (0xfffffff8u, 30), (0xffffff30u, 28),
		(0xffffff40u, 28), (0xffffff50u, 28), (0xffffff60u, 28), (0xffffff70u, 28),
		(0xffffff80u, 28), (0xffffff90u, 28), (0xffffffa0u, 28), (0xffffffb0u, 28),
		(0x50000000u, 6), (0xfe000000u, 10), (0xfe400000u, 10), (0xffa00000u, 12),
		(0xffc80000u, 13), (0x54000000u, 6), (0xf8000000u, 8), (0xff400000u, 11),
		(0xfe800000u, 10), (0xfec00000u, 10), (0xf9000000u, 8), (0xff600000u, 11),
		(0xfa000000u, 8), (0x58000000u, 6), (0x5c000000u, 6), (0x60000000u, 6),
		(0x0u, 5), (0x8000000u, 5), (0x10000000u, 5), (0x64000000u, 6),
		(0x68000000u, 6), (0x6c000000u, 6), (0x70000000u, 6), (0x74000000u, 6),
		(0x78000000u, 6), (0x7c000000u, 6), (0xb8000000u, 7), (0xfb000000u, 8),
		(0xfff80000u, 15), (0x80000000u, 6), (0xffb00000u, 12), (0xff000000u, 10),
		(0xffd00000u, 13), (0x84000000u, 6), (0xba000000u, 7), (0xbc000000u, 7),
		(0xbe000000u, 7), (0xc0000000u, 7), (0xc2000000u, 7), (0xc4000000u, 7),
		(0xc6000000u, 7), (0xc8000000u, 7), (0xca000000u, 7), (0xcc000000u, 7),
		(0xce000000u, 7), (0xd0000000u, 7), (0xd2000000u, 7), (0xd4000000u, 7),
		(0xd6000000u, 7), (0xd8000000u, 7), (0xda000000u, 7), (0xdc000000u, 7),
		(0xde000000u, 7), (0xe0000000u, 7), (0xe2000000u, 7), (0xe4000000u, 7),
		(0xfc000000u, 8), (0xe6000000u, 7), (0xfd000000u, 8), (0xffd80000u, 13),
		(0xfffe0000u, 19), (0xffe00000u, 13), (0xfff00000u, 14), (0x88000000u, 6),
		(0xfffa0000u, 15), (0x18000000u, 5), (0x8c000000u, 6), (0x20000000u, 5),
		(0x90000000u, 6), (0x28000000u, 5), (0x94000000u, 6), (0x98000000u, 6),
		(0x9c000000u, 6), (0x30000000u, 5), (0xe8000000u, 7), (0xea000000u, 7),
		(0xa0000000u, 6), (0xa4000000u, 6), (0xa8000000u, 6), (0x38000000u, 5),
		(0xac000000u, 6), (0xec000000u, 7), (0xb0000000u, 6), (0x40000000u, 5),
		(0x48000000u, 5), (0xb4000000u, 6), (0xee000000u, 7), (0xf0000000u, 7),
		(0xf2000000u, 7), (0xf4000000u, 7), (0xf6000000u, 7), (0xfffc0000u, 15),
		(0xff800000u, 11), (0xfff40000u, 14), (0xffe80000u, 13), (0xffffffc0u, 28),
		(0xfffe6000u, 20), (0xffff4800u, 22), (0xfffe7000u, 20), (0xfffe8000u, 20),
		(0xffff4c00u, 22), (0xffff5000u, 22), (0xffff5400u, 22), (0xffffb200u, 23),
		(0xffff5800u, 22), (0xffffb400u, 23), (0xffffb600u, 23), (0xffffb800u, 23),
		(0xffffba00u, 23), (0xffffbc00u, 23), (0xffffeb00u, 24), (0xffffbe00u, 23),
		(0xffffec00u, 24), (0xffffed00u, 24), (0xffff5c00u, 22), (0xffffc000u, 23),
		(0xffffee00u, 24), (0xffffc200u, 23), (0xffffc400u, 23), (0xffffc600u, 23),
		(0xffffc800u, 23), (0xfffee000u, 21), (0xffff6000u, 22), (0xffffca00u, 23),
		(0xffff6400u, 22), (0xffffcc00u, 23), (0xffffce00u, 23), (0xffffef00u, 24),
		(0xffff6800u, 22), (0xfffee800u, 21), (0xfffe9000u, 20), (0xffff6c00u, 22),
		(0xffff7000u, 22), (0xffffd000u, 23), (0xffffd200u, 23), (0xfffef000u, 21),
		(0xffffd400u, 23), (0xffff7400u, 22), (0xffff7800u, 22), (0xfffff000u, 24),
		(0xfffef800u, 21), (0xffff7c00u, 22), (0xffffd600u, 23), (0xffffd800u, 23),
		(0xffff0000u, 21), (0xffff0800u, 21), (0xffff8000u, 22), (0xffff1000u, 21),
		(0xffffda00u, 23), (0xffff8400u, 22), (0xffffdc00u, 23), (0xffffde00u, 23),
		(0xfffea000u, 20), (0xffff8800u, 22), (0xffff8c00u, 22), (0xffff9000u, 22),
		(0xffffe000u, 23), (0xffff9400u, 22), (0xffff9800u, 22), (0xffffe200u, 23),
		(0xfffff800u, 26), (0xfffff840u, 26), (0xfffeb000u, 20), (0xfffe2000u, 19),
		(0xffff9c00u, 22), (0xffffe400u, 23), (0xffffa000u, 22), (0xfffff600u, 25),
		(0xfffff880u, 26), (0xfffff8c0u, 26), (0xfffff900u, 26), (0xfffffbc0u, 27),
		(0xfffffbe0u, 27), (0xfffff940u, 26), (0xfffff100u, 24), (0xfffff680u, 25),
		(0xfffe4000u, 19), (0xffff1800u, 21), (0xfffff980u, 26), (0xfffffc00u, 27),
		(0xfffffc20u, 27), (0xfffff9c0u, 26), (0xfffffc40u, 27), (0xfffff200u, 24),
		(0xffff2000u, 21), (0xffff2800u, 21), (0xfffffa00u, 26), (0xfffffa40u, 26),
		(0xffffffd0u, 28), (0xfffffc60u, 27), (0xfffffc80u, 27), (0xfffffca0u, 27),
		(0xfffec000u, 20), (0xfffff300u, 24), (0xfffed000u, 20), (0xffff3000u, 21),
		(0xffffa400u, 22), (0xffff3800u, 21), (0xffff4000u, 21), (0xffffe600u, 23),
		(0xffffa800u, 22), (0xffffac00u, 22), (0xfffff700u, 25), (0xfffff780u, 25),
		(0xfffff400u, 24), (0xfffff500u, 24), (0xfffffa80u, 26), (0xffffe800u, 23),
		(0xfffffac0u, 26), (0xfffffcc0u, 27), (0xfffffb00u, 26), (0xfffffb40u, 26),
		(0xfffffce0u, 27), (0xfffffd00u, 27), (0xfffffd20u, 27), (0xfffffd40u, 27),
		(0xfffffd60u, 27), (0xffffffe0u, 28), (0xfffffd80u, 27), (0xfffffda0u, 27),
		(0xfffffdc0u, 27), (0xfffffde0u, 27), (0xfffffe00u, 27), (0xfffffb80u, 26),
		(0xfffffffcu, 30),
	];

	/// <summary>Maps (length, code) to the symbol, for decoding one bit at a time.</summary>
	static readonly Dictionary<(int Length, uint Code), int> BySymbol = Build();

	static Dictionary<(int Length, uint Code), int> Build() {
		var map = new Dictionary<(int, uint), int>();
		for (int symbol = 0; symbol < 256; symbol++) {
			var (code, length) = Table[symbol];
			map[(length, code >> (32 - length))] = symbol;
		}
		return map;
	}

	/// <summary>Decodes a Huffman-coded string. Throws <see cref="InvalidDataException"/> if the bytes are not valid HPACK Huffman data.</summary>
	public static byte[] Decode(ReadOnlySpan<byte> encoded) {
		var output = new List<byte>(encoded.Length * 2);
		uint code = 0;
		int length = 0;
		foreach (byte b in encoded) {
			for (int bit = 7; bit >= 0; bit--) {
				code = (code << 1) | (uint)((b >> bit) & 1);
				length++;
				if (BySymbol.TryGetValue((length, code), out int symbol)) {
					output.Add((byte)symbol);
					code = 0;
					length = 0;
				}
				else if (length > 30) throw new InvalidDataException("Invalid Huffman code in an HPACK string.");
			}
		}
		// What is left must be padding: fewer than 8 bits, all ones (a prefix of the end-of-string code).
		if (length > 7 || code != (1u << length) - 1) throw new InvalidDataException("Invalid padding at the end of an HPACK Huffman string.");
		return output.ToArray();
	}
}

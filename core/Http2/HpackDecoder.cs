using System.Text;

namespace MoSaProxy;

/// <summary>
/// Decodes HPACK header blocks (RFC 7541) for one direction of one HTTP/2 connection. The dynamic table is part of the
/// connection's state, so every header block of the connection must be passed in, in order, even for streams nobody wants.
/// Header text is read as Latin-1, so every byte survives unchanged.
/// </summary>
sealed class HpackDecoder {
	const int EntryOverhead = 32;
	readonly LinkedList<(string Name, string Value)> _dynamic = new();   // newest first
	int _dynamicSize;
	int _maxDynamicSize = 4096;

	/// <summary>Decodes one complete header block into its header fields, in the order they appear.</summary>
	/// <exception cref="InvalidDataException">The block is not valid HPACK.</exception>
	public List<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block) {
		var headers = new List<KeyValuePair<string, string>>();
		int pos = 0;
		while (pos < block.Length) {
			byte first = block[pos];
			if ((first & 0x80) != 0) {                           // indexed header field
				int index = ReadInteger(block, ref pos, 7);
				var (name, value) = Lookup(index);
				headers.Add(KeyValuePair.Create(name, value));
			}
			else if ((first & 0xC0) == 0x40) {                   // literal, added to the dynamic table
				var (name, value) = ReadLiteral(block, ref pos, 6);
				Add(name, value);
				headers.Add(KeyValuePair.Create(name, value));
			}
			else if ((first & 0xE0) == 0x20) {                   // dynamic table size update
				int size = ReadInteger(block, ref pos, 5);
				_maxDynamicSize = size;
				Evict();
			}
			else {                                               // literal, not added (with or without "never indexed")
				var (name, value) = ReadLiteral(block, ref pos, 4);
				headers.Add(KeyValuePair.Create(name, value));
			}
		}
		return headers;
	}

	(string Name, string Value) Lookup(int index) {
		if (index <= 0) throw new InvalidDataException("HPACK index 0 is not valid.");
		if (index <= HpackStaticTable.Count) return HpackStaticTable.Get(index);
		int dynamicIndex = index - HpackStaticTable.Count - 1;
		if (dynamicIndex >= _dynamic.Count) throw new InvalidDataException("HPACK index is past the end of the dynamic table.");
		return _dynamic.ElementAt(dynamicIndex);
	}

	(string Name, string Value) ReadLiteral(ReadOnlySpan<byte> block, ref int pos, int prefixBits) {
		int nameIndex = ReadInteger(block, ref pos, prefixBits);
		string name = nameIndex == 0 ? ReadString(block, ref pos) : Lookup(nameIndex).Name;
		string value = ReadString(block, ref pos);
		return (name, value);
	}

	void Add(string name, string value) {
		_dynamic.AddFirst((name, value));
		_dynamicSize += name.Length + value.Length + EntryOverhead;
		Evict();
	}

	void Evict() {
		while (_dynamicSize > _maxDynamicSize && _dynamic.Last is { } oldest) {
			_dynamicSize -= oldest.Value.Name.Length + oldest.Value.Value.Length + EntryOverhead;
			_dynamic.RemoveLast();
		}
	}

	static int ReadInteger(ReadOnlySpan<byte> block, ref int pos, int prefixBits) {
		if (pos >= block.Length) throw new InvalidDataException("HPACK block ended inside an integer.");
		int max = (1 << prefixBits) - 1;
		long value = block[pos++] & max;
		if (value < max) return (int)value;
		int shift = 0;
		while (true) {
			if (pos >= block.Length || shift > 28) throw new InvalidDataException("HPACK integer is truncated or too large.");
			byte next = block[pos++];
			value += (long)(next & 0x7F) << shift;
			shift += 7;
			if ((next & 0x80) == 0) break;
		}
		if (value > int.MaxValue) throw new InvalidDataException("HPACK integer is too large.");
		return (int)value;
	}

	static string ReadString(ReadOnlySpan<byte> block, ref int pos) {
		if (pos >= block.Length) throw new InvalidDataException("HPACK block ended before a string.");
		bool huffman = (block[pos] & 0x80) != 0;
		int length = ReadInteger(block, ref pos, 7);
		if (pos + length > block.Length) throw new InvalidDataException("HPACK string is longer than the block.");
		var bytes = block.Slice(pos, length);
		pos += length;
		return Encoding.Latin1.GetString(huffman ? HpackHuffman.Decode(bytes) : bytes);
	}
}

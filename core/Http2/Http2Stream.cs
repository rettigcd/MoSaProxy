namespace MoSaProxy;

/// <summary>What one direction of an HTTP/2 connection has carried for one stream: its decoded header blocks and its body bytes.</summary>
sealed class Http2Stream {
	/// <summary>Decoded header blocks in the order they arrived: the head first (for a response, possibly after 1xx interim heads), then any trailers.</summary>
	public List<List<KeyValuePair<string, string>>> HeaderBlocks { get; } = new();

	/// <summary>The DATA frames' payloads, in order (padding removed).</summary>
	public MemoryStream Body { get; } = new();

	/// <summary>True once the stream was ended by END_STREAM or reset.</summary>
	public bool Ended { get; set; }
}

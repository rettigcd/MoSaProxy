namespace MoSaProxy;

/// <summary>Receives the bytes of one proxy-to-origin connection as they are written and read.</summary>
interface IOriginTap {
	/// <summary>Bytes written to the origin, with the exchange that is current on the writing flow (if any).</summary>
	void Wrote(ExchangeWire? exchange, ReadOnlySpan<byte> bytes);

	/// <summary>Bytes read from the origin.</summary>
	void Received(ReadOnlySpan<byte> bytes);

	/// <summary>The connection was closed.</summary>
	void Closed();
}

namespace MoSaProxy;

/// <summary>
/// Capture that can be switched on and off while the proxy runs. An exchange that begins while it is on is captured to its end,
/// even if it is switched off meanwhile; one that begins while it is off is not captured at all, even if it is switched on
/// meanwhile. So no exchange is ever half written. Numbers continue where they left off, because the exchange count only advances
/// when an exchange is captured.
/// </summary>
public sealed class SwitchableSessionCapture(ISessionCapture inner, bool enabled = true) : ISessionCapture {
	volatile bool _enabled = enabled;

	/// <summary>Whether exchanges that begin now are captured.</summary>
	public bool Enabled {
		get => _enabled;
		set => _enabled = value;
	}

	public IExchangeCapture BeginExchange() => _enabled ? inner.BeginExchange() : NullExchangeCapture.Instance;
}

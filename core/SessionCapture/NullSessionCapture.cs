namespace MoSaProxy;

/// <summary>Source of per-exchange capture files (Fiddler-style N_c.txt / N_s.txt / N_m.xml).</summary>
public interface ISessionCapture {
	/// <summary>Starts capturing a new exchange. The exchange number is assigned here, in arrival order.</summary>
	IExchangeCapture BeginExchange();
}

public sealed class NullSessionCapture : ISessionCapture {
	public static readonly NullSessionCapture Instance = new();
	public IExchangeCapture BeginExchange() => NullExchangeCapture.Instance;
}

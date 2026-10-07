namespace MoSaProxy;

/// <summary>What was still running when a shutdown's drain period ended and had to be aborted. All zeros means everything finished by itself.</summary>
public sealed record ShutdownReport(int Exchanges, int Tunnels, int WebSockets) {
	public bool Drained => Exchanges + Tunnels + WebSockets == 0;
}

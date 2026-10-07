namespace MoSaProxy;

/// <summary>What happened on one proxied WebSocket connection (Status is the HTTP status of the handshake; 101 means it was established).</summary>
public sealed record WebSocketSummary(int Status, long ClientBytes, long OriginBytes, int ClientMessages, int OriginMessages, string? Error);

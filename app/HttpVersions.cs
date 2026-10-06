namespace mos;

public static class HttpVersions {
	/// <summary>Formats a protocol version the way it is logged and captured: "HTTP/1.0", "HTTP/1.1", "HTTP/2", "HTTP/3".</summary>
	public static string Format(Version version) =>
		version.Major >= 2 && version.Minor == 0 ? $"HTTP/{version.Major}" : $"HTTP/{version.Major}.{version.Minor}";
}

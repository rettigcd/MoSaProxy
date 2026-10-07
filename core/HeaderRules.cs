namespace MoSaProxy;

public static class HeaderRules {

	static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase) {
		"Connection", "Proxy-Connection", "Keep-Alive", "Proxy-Authenticate",
		"Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade",
	};

	/// <summary>Headers that must not be forwarded: the fixed hop-by-hop set plus any header named by Connection.</summary>
	public static HashSet<string> HeadersToDrop(IHeaderDictionary headers) {
		var drop = new HashSet<string>(HopByHop, StringComparer.OrdinalIgnoreCase);
		foreach (var value in headers.Connection)
			foreach (var token in (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
				drop.Add(token);
		return drop;
	}

	public static bool IsHopByHop(string name) => HopByHop.Contains(name);

	/// <summary>Flattens headers for logging. Values are logged as-is, including credentials and cookies.</summary>
	public static Dictionary<string, string> ForLog(IHeaderDictionary headers) {
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, values) in headers)
			result[name] = values.ToString();
		return result;
	}

	/// <summary>Flattens an already-parsed header list for logging (duplicate names are joined with ", ").</summary>
	public static Dictionary<string, string> ForLog(IEnumerable<KeyValuePair<string, string>> headers) {
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value) in headers)
			result[name] = result.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
		return result;
	}
}

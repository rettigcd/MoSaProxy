using System.Text.Json;

namespace MoSaProxy;

/// <param name="onFailure">Called with the exception when a sink fails to write (the other sinks still get the record).</param>
public sealed class CompositeExchangeLog(IExchangeLog[] logs, Action<Exception>? onFailure = null) : IExchangeLog {
	public void Write(ExchangeRecord record) {
		foreach (var log in logs) {
			try { log.Write(record); }
			catch (Exception ex) {
				Console.Error.WriteLine($"Log sink failed: {ex.Message}");
				onFailure?.Invoke(ex);
			}
		}
	}
}

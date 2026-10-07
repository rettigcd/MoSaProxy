using System.Net.Sockets;
using System.Text;

namespace MoSaProxy.Tests;

public static class RawHttp {
	/// <summary>Sends literal request text to a loopback port and returns everything the server answers until it closes.</summary>
	public static async Task<string> SendAsync(int port, string requestText) {
		using var tcp = new TcpClient();
		await tcp.ConnectAsync("127.0.0.1", port);
		var stream = tcp.GetStream();
		await stream.WriteAsync(Encoding.ASCII.GetBytes(requestText));
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		using var reader = new MemoryStream();
		await stream.CopyToAsync(reader, cts.Token);
		return Encoding.UTF8.GetString(reader.ToArray());
	}
}

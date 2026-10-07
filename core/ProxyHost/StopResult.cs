using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MoSaProxy;

/// <summary>Why <see cref="ProxyHost.RunUntilStoppedAsync"/> stopped the proxy.</summary>
public enum StopReason {
	/// <summary>Something asked the application to stop (Ctrl+C, a termination signal, or <see cref="ProxyHost.RequestStop"/>).</summary>
	StopRequested,
	/// <summary>A limit was hit (disk full, out of memory, out of sockets...) and the proxy stopped itself at once.</summary>
	LimitReached,
}

/// <summary>How a run ended.</summary>
/// <param name="Reason">What made the proxy stop.</param>
/// <param name="Report">What was still running when the drain period ended and had to be aborted.</param>
/// <param name="Limit">The first limit that was hit during the run (even one hit while draining), or null if none was.</param>
public sealed record StopResult(StopReason Reason, ShutdownReport Report, string? Limit);

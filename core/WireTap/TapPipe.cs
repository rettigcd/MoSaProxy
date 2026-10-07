using System.IO.Pipelines;

namespace MoSaProxy;

/// <summary>A pipe whose reader and writer are tapped, used to replace a connection's transport.</summary>
sealed class TapPipe(PipeReader input, PipeWriter output) : IDuplexPipe {
	public PipeReader Input { get; } = input;
	public PipeWriter Output { get; } = output;
}

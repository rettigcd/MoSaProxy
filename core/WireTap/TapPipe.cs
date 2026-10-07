using System.IO.Pipelines;

namespace MoSaProxy;

/// <summary>A pipe whose reader is tapped, used to replace a connection's transport. Only what the client sends is recorded.</summary>
sealed class TapPipe(PipeReader input, PipeWriter output) : IDuplexPipe {
	public PipeReader Input { get; } = input;
	public PipeWriter Output { get; } = output;
}

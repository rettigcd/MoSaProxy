using System.IO.Pipelines;

namespace MoSaProxy;

/// <summary>A simple IDuplexPipe made of one reader and one writer.</summary>
sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe {
	public PipeReader Input => input;
	public PipeWriter Output => output;
}

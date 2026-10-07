using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace MoSaProxy;

/// <summary>A simple IDuplexPipe made of one reader and one writer.</summary>
sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe {
	public PipeReader Input => input;
	public PipeWriter Output => output;
}

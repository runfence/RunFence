using System.IO.Pipes;
using RunFence.Ipc;

namespace RunFence.Tests;

/// <summary>
/// Test double for <see cref="IIpcIdentityExtractor"/> that always returns the same
/// <see cref="IpcCallerContext"/> regardless of the pipe passed.
/// </summary>
internal sealed class FixedIpcIdentityExtractor(IpcCallerContext context) : IIpcIdentityExtractor
{
    public IpcCallerContext Extract(NamedPipeServerStream pipe) => context;
}

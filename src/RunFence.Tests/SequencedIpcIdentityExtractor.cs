using System.IO.Pipes;
using RunFence.Ipc;

namespace RunFence.Tests;

/// <summary>
/// Test double for <see cref="IIpcIdentityExtractor"/> that returns successive contexts per call,
/// wrapping around to the beginning when the sequence is exhausted.
/// </summary>
internal sealed class SequencedIpcIdentityExtractor(IpcCallerContext[] contexts) : IIpcIdentityExtractor
{
    private int _index;

    public IpcCallerContext Extract(NamedPipeServerStream pipe)
    {
        var context = contexts[_index % contexts.Length];
        _index++;
        return context;
    }
}

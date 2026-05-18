using System.IO.Pipes;

namespace RunFence.Infrastructure;

public sealed class JobKeeperState(NamedPipeServerStream pipe, int pid)
    : IDisposable
{
    public NamedPipeServerStream Pipe { get; } = pipe;
    public int Pid { get; } = pid;
    public object SyncRoot { get; } = new();
    public SemaphoreSlim IpcGate { get; } = new(1, 1);

    public void Dispose()
    {
        try { Pipe.Dispose(); } catch { }
        IpcGate.Dispose();
    }
}

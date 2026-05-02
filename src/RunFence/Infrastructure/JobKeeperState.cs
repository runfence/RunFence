using System.IO.Pipes;

namespace RunFence.Infrastructure;

public sealed class JobKeeperState(NamedPipeServerStream pipe, int pid)
{
    public NamedPipeServerStream Pipe { get; } = pipe;
    public int Pid { get; } = pid;
    public object SyncRoot { get; } = new();
}

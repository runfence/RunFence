using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public interface IIpcServerService : IDisposable
{
    void Start(Func<IpcMessage, string?, string?, bool, IpcResponse> handler);
    void Stop();
}
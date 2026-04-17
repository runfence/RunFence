using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public interface IIpcServerService : IDisposable
{
    void Start(Func<IpcMessage, IpcCallerContext, IpcResponse> handler);
    void Stop();
}
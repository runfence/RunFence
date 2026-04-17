using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public interface IIpcMessageHandler
{
    IpcResponse HandleIpcMessage(IpcMessage message, IpcCallerContext context);
}
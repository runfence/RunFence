using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public interface IIpcMessageHandler
{
    IpcResponse HandleIpcMessage(IpcMessage message, string? callerIdentity, string? callerSid, bool isAdmin);
}
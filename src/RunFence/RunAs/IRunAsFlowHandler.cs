using RunFence.Core.Ipc;

namespace RunFence.RunAs;

public interface IRunAsFlowHandler
{
    IpcResponse HandleRunAs(IpcMessage message, string? callerIdentity, string? callerSid, bool isAdmin);
}
using RunFence.Core.Ipc;
using RunFence.Ipc;

namespace RunFence.RunAs;

public interface IRunAsFlowHandler
{
    IpcResponse HandleRunAs(IpcMessage message, IpcCallerContext context);
}
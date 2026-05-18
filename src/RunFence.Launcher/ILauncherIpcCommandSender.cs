using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public interface ILauncherIpcCommandSender
{
    IpcResponse? SendWithAutoStart(IpcMessage message);
}

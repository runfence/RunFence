using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public sealed class LauncherIpcCommandSender(LauncherIpcHelper ipcHelper) : ILauncherIpcCommandSender
{
    public IpcResponse? SendWithAutoStart(IpcMessage message) =>
        ipcHelper.SendWithAutoStart(message);
}

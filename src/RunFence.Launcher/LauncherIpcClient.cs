using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// <see cref="ILauncherIpcClient"/> implementation for the Launcher process, using the longer
/// <see cref="IpcConstants.LauncherTimeoutMs"/> connect timeout instead of the default pipe timeout.
/// </summary>
public class LauncherIpcClient : ILauncherIpcClient
{
    public IpcResponse? SendMessage(IpcMessage message) =>
        IpcClient.SendMessage(message, IpcConstants.LauncherTimeoutMs);

    public bool PingServer() => IpcClient.PingServer();
}

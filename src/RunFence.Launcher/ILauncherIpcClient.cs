using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Extends <see cref="IIpcClient"/> with ping capability needed by the Launcher to check
/// whether the RunFence server is responding before sending a full message.
/// </summary>
public interface ILauncherIpcClient : IIpcClient
{
    /// <summary>Pings the RunFence server to check if it is responding.</summary>
    bool PingServer();
}

using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

/// <summary>
/// Pipe creation, process launch, and pipe protocol operations for the drag-bridge copy flow.
/// Used by <see cref="DragBridgeCopyFlow"/>.
/// </summary>
public interface IDragBridgePipeLauncher
{
    NamedPipeServerStream CreatePipeServer(string pipeName, SecurityIdentifier targetUserSid);
    ProcessInfo? LaunchForSid(WindowOwnerInfo ownerInfo, IReadOnlyList<string> args, INotificationService notifications);
    void SignalReady(NamedPipeServerStream pipe);
    bool VerifyClientProcess(NamedPipeServerStream pipe, ProcessInfo? expectedProcess);
    void SetActiveProcess(ProcessInfo? process);
    void KillProcess(ProcessInfo? process);
}

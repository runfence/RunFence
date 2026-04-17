using RunFence.Core.Models;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

/// <summary>
/// Session-level management for the drag-bridge operation: lifecycle, cancellation, and active process tracking.
/// Used by <see cref="DragBridgeService"/>.
/// </summary>
public interface IDragBridgeSessionManager
{
    void SetData(SessionContext session);
    CancellationTokenSource BeginOperation();
    void KillActiveOperation();
    void SetActiveProcess(ProcessInfo? process);
    void KillProcess(ProcessInfo? process);
}

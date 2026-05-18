using RunFence.Core.Ipc;
using RunFence.Ipc;

namespace RunFence.RunAs;

public interface IRunAsFlowHandler
{
    IpcResponse HandleRunAs(IpcMessage message, IpcCallerContext context);

    /// <summary>
    /// Triggers the RunAs flow directly from the UI (no IPC authorization check).
    /// Must be called on the UI thread after the caller has already obtained the file path.
    /// </summary>
    void TriggerFromUI(string filePath, string? initialAccountSid = null);

    /// <summary>
    /// Async RunAs flow entrypoint for UI callers. Returns when the RunAs workflow completes
    /// or when a guard rejects the request before the dialog opens.
    /// </summary>
    Task TriggerFromUIAsync(string filePath, string? initialAccountSid = null);
}

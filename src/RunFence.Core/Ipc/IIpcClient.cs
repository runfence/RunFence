namespace RunFence.Core.Ipc;

/// <summary>
/// Abstraction over IPC message sending, enabling testability of pre-DI startup code
/// that cannot use constructor injection.
/// </summary>
public interface IIpcClient
{
    /// <summary>Sends an IPC message to the RunFence server. Returns null if the server is not running.</summary>
    IpcResponse? SendMessage(IpcMessage message);
}

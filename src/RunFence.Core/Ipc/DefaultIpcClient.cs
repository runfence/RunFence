namespace RunFence.Core.Ipc;

/// <summary>
/// Default implementation of <see cref="IIpcClient"/> that delegates to the static <see cref="IpcClient"/>.
/// </summary>
public class DefaultIpcClient : IIpcClient
{
    public IpcResponse? SendMessage(IpcMessage message) => IpcClient.SendMessage(message);
}

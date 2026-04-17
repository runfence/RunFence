namespace RunFence.Core.Ipc;

public class IpcResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public IpcErrorCode ErrorCode { get; set; }
}
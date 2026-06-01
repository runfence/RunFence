namespace RunFence.DragBridge;

public interface IWindowOwnerProcessTokenReader
{
    bool TryGetTokenInfo(uint processId, out WindowOwnerProcessTokenInfo info);
}

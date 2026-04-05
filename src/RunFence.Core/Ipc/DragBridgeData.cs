namespace RunFence.Core.Ipc;

public enum DragBridgeMessageType
{
    FileList,
    ResolveRequest
}

public class DragBridgeData
{
    public DragBridgeMessageType MessageType { get; set; } = DragBridgeMessageType.FileList;
    public List<string> FilePaths { get; set; } = [];
}
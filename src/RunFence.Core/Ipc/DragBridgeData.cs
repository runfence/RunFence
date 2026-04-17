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

    /// <summary>
    /// When true in the initial FileList message, the window starts with files already resolved
    /// (no ResolveRequest needed before dragging). Set when a pre-check confirms the target
    /// already has access to all captured files.
    /// </summary>
    public bool FilesResolved { get; set; }
}
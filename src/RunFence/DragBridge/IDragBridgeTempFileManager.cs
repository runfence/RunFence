namespace RunFence.DragBridge;

public interface IDragBridgeTempFileManager
{
    DragBridgeTempFolderResult CreateTempFolder(string targetSid, string? containerSid = null);
    DragBridgeTempFileResult CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths);
    void CleanupOldFolders(TimeSpan maxAge);
}

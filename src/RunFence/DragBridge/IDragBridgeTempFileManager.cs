namespace RunFence.DragBridge;

public interface IDragBridgeTempFileManager
{
    string CreateTempFolder(string targetSid, string? containerSid = null);
    List<string> CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths);
    void CleanupOldFolders(TimeSpan maxAge);
}
namespace RunFence.DragBridge;

public interface IDragBridgeTempFileManager
{
    /// <summary>
    /// Raised when traverse ACEs are granted on ancestor directories for a SID.
    /// Subscribers (e.g. DragBridgeService) can record the grant in the database.
    /// </summary>
    event Action<string, List<string>>? TraverseGranted;

    string CreateTempFolder(string targetSid, string? containerSid = null);
    List<string> CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths);
    void CleanupOldFolders(TimeSpan maxAge);
}
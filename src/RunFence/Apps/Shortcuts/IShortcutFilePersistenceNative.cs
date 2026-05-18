namespace RunFence.Apps.Shortcuts;

public interface IShortcutFilePersistenceNative
{
    ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath);
    void DeleteExistingDestination(string shortcutPath);
    void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata);
}

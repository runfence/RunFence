namespace RunFence.Apps.Shortcuts;

public interface IShortcutFilePersistenceService
{
    void PersistShortcut(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode);
}

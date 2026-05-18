namespace RunFence.Apps.Shortcuts;

public class ShortcutWriteAccessService(
    IShortcutFilePersistenceService filePersistenceService) : IShortcutWriteAccessService
{
    public void Save(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode)
        => filePersistenceService.PersistShortcut(shortcutPath, mutation, metadataMode, contentMode);
}

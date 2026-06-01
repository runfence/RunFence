namespace RunFence.Apps.Shortcuts;

public sealed class ManagedShortcutLifecycleService(
    IShortcutFilePersistenceNative shortcutFilePersistenceNative,
    IShortcutWriteAccessService shortcutWriteAccessService) : IManagedShortcutLifecycleService
{
    public void DeleteManagedShortcutFile(string shortcutPath)
    {
        shortcutFilePersistenceNative.DeleteExistingDestination(shortcutPath);
    }

    public void RewriteManagedShortcutFile(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode)
    {
        shortcutWriteAccessService.Save(shortcutPath, mutation, metadataMode, contentMode);
    }
}

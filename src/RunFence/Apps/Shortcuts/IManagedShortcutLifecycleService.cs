namespace RunFence.Apps.Shortcuts;

public interface IManagedShortcutLifecycleService
{
    void DeleteManagedShortcutFile(string shortcutPath);
    void RewriteManagedShortcutFile(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode);
}

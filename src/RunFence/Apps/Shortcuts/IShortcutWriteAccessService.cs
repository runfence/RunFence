namespace RunFence.Apps.Shortcuts;

public interface IShortcutWriteAccessService
{
    void Save(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode);
}

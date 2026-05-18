namespace RunFence.Apps.Shortcuts;

public interface IShortcutProtectionStateStore
{
    ShortcutProtectionState? Load(string shortcutPath);
    void Save(ShortcutProtectionState state);
    void Delete(string shortcutPath);
}

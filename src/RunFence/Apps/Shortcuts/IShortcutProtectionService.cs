namespace RunFence.Apps.Shortcuts;

public interface IShortcutProtectionService
{
    void ProtectShortcut(string shortcutPath);
    void UnprotectShortcut(string shortcutPath);
    void ProtectInternalShortcut(string shortcutPath, string accountSid);
}
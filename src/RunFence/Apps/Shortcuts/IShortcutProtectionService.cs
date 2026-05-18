namespace RunFence.Apps.Shortcuts;

public interface IShortcutProtectionService
{
    void ProtectShortcut(string shortcutPath, bool allowAdministratorsDelete = false);
    void UnprotectShortcut(string shortcutPath);
    void ProtectInternalShortcut(string shortcutPath, string accountSid);
}

namespace RunFence.Apps.Shortcuts;

public interface IShortcutProtectionService
{
    void ProtectShortcut(
        string appId,
        string shortcutPath,
        bool allowAdministratorsDelete = false);
    void UnprotectShortcut(string appId, string shortcutPath);
    void ProtectInternalShortcut(string appId, string shortcutPath, string accountSid);
}

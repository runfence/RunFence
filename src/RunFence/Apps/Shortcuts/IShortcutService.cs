using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutService
{
    void ReplaceShortcuts(AppEntry app, string launcherPath, string iconPath, ShortcutTraversalCache cache);
    void RevertShortcuts(AppEntry app, ShortcutTraversalCache cache);
    void SaveShortcut(AppEntry app, string shortcutPath);
    void UpdateShortcutToLauncher(string shortcutPath, string appId, string launcherPath, string? iconPath);
    bool RevertSingleShortcut(string shortcutPath, AppEntry app);
    void EnforceShortcuts(IEnumerable<AppEntry> apps, string launcherPath, ShortcutTraversalCache cache);
}

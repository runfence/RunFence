using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutService : IShortcutDiscoveryService
{
    void ReplaceShortcuts(AppEntry app, string launcherPath, string iconPath);
    void RevertShortcuts(AppEntry app);
    void SaveShortcut(AppEntry app, string shortcutPath);
    void UpdateShortcutToLauncher(string shortcutPath, string appId, string launcherPath, string? iconPath);
    bool RevertSingleShortcut(string shortcutPath, AppEntry app);
    void EnforceShortcuts(IEnumerable<AppEntry> apps, string launcherPath);
    void CreateBesideTargetShortcut(AppEntry app, string launcherPath, string iconPath, string username);
    void RemoveBesideTargetShortcut(AppEntry app);

    void EnforceBesideTargetShortcuts(IEnumerable<AppEntry> apps, string launcherPath,
        Func<AppEntry, (string username, string iconPath)?> resolveAppInfo);
}
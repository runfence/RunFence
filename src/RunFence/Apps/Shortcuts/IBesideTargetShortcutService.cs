using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IBesideTargetShortcutService
{
    void CreateBesideTargetShortcut(AppEntry app, string launcherPath, string iconPath, string username);
    void RemoveBesideTargetShortcut(AppEntry app);

    void EnforceBesideTargetShortcuts(IEnumerable<AppEntry> apps, string launcherPath,
        Func<AppEntry, (string username, string iconPath)?> resolveAppInfo);
}
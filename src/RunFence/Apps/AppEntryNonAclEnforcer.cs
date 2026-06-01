using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps;

public class AppEntryNonAclEnforcer(
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IIconService iconService,
    ISidNameCacheService sidNameCache,
    IInteractiveUserDesktopProvider desktopProvider,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    IRunFenceLauncherPathProvider launcherPathProvider,
    ILoggingService log)
{
    public void ApplyAll(AppEntry app, ShortcutTraversalCache shortcutCache)
    {
        var iconPath = RefreshBadgedIcon(app);
        ReplaceManagedShortcuts(app, shortcutCache, iconPath);
        ApplyBesideTargetShortcut(app, iconPath);
    }

    public void ApplyTargeted(
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        ApplyTargeted(app, shortcutCache, changeSet, iconPathOverrideForBesideTargetShortcut: null);
    }

    public void ApplyTargeted(
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet,
        string? iconPathOverrideForBesideTargetShortcut)
    {
        string? iconPath = iconPathOverrideForBesideTargetShortcut;
        if (changeSet.RequiresIconRefresh && string.IsNullOrEmpty(iconPath))
            iconPath = RefreshBadgedIcon(app);

        if (changeSet.RequiresManagedShortcutRefresh)
            ReplaceManagedShortcuts(app, shortcutCache, iconPath);

        if (changeSet.RequiresBesideTargetRefresh ||
            (changeSet.RequiresIconRefresh && !changeSet.RequiresManagedShortcutRefresh))
        {
            ApplyBesideTargetShortcut(app, iconPath);
        }
    }

    public void RevertAll(AppEntry app, ShortcutTraversalCache shortcutCache)
    {
        RevertManagedShortcuts(app, shortcutCache);
        RevertBesideTargetShortcut(app);
    }

    public void RevertTargeted(
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        AppEntryChangeSet changeSet)
    {
        if (changeSet.RequiresManagedShortcutRefresh)
            RevertManagedShortcuts(app, shortcutCache);

        if (changeSet.RequiresBesideTargetRefresh)
            RevertBesideTargetShortcut(app);
    }

    public void CreateDesktopShortcut(AppEntry app)
    {
        if (app.IsUrlScheme)
            return;

        var desktop = desktopProvider.GetDesktopPath();
        if (desktop == null)
            return;

        shortcutService.SaveShortcut(app, Path.Combine(desktop, $"{app.Name}.lnk"));
    }

    private string RefreshBadgedIcon(AppEntry app)
    {
        var iconPath = string.Empty;
        if (app.ManageShortcuts || !app.IsUrlScheme)
            iconPath = iconService.CreateBadgedIcon(app);

        if (app is { IsUrlScheme: false, IsFolder: false } && File.Exists(app.ExePath))
            app.LastKnownExeTimestamp = File.GetLastWriteTimeUtc(app.ExePath);
        else if (app.IsFolder)
            app.LastKnownExeTimestamp = null;

        return iconPath;
    }

    private void ApplyBesideTargetShortcut(AppEntry app, string? iconPath)
    {
        if (app.IsUrlScheme || !launcherPathProvider.Exists())
            return;

        var launcherPath = launcherPathProvider.GetLauncherPath();
        var effectiveSid = app.AppContainerName != null
            ? interactiveUserSidResolver.GetInteractiveUserSid()
            : app.AccountSid;
        if (!string.IsNullOrEmpty(effectiveSid))
        {
            var username = sidNameCache.GetDisplayName(effectiveSid);
            if (!string.Equals(username, effectiveSid, StringComparison.OrdinalIgnoreCase))
                besideTargetShortcutService.CreateBesideTargetShortcut(app, launcherPath, iconPath ?? string.Empty, username);
        }
        else if (app.AppContainerName != null)
        {
            log.Warn($"AppEntryNonAclEnforcer: interactive user SID unavailable; skipping AppContainer beside-target shortcut for '{app.Name}'.");
        }
    }

    private void ReplaceManagedShortcuts(
        AppEntry app,
        ShortcutTraversalCache shortcutCache,
        string? iconPath)
    {
        if (app.ManageShortcuts && launcherPathProvider.Exists())
        {
            shortcutService.ReplaceShortcuts(
                app,
                launcherPathProvider.GetLauncherPath(),
                iconPath ?? iconService.GetIconPath(app.Id),
                shortcutCache);
        }
    }

    private void RevertBesideTargetShortcut(AppEntry app)
    {
        if (!app.IsUrlScheme)
            besideTargetShortcutService.RemoveBesideTargetShortcut(app);
    }

    private void RevertManagedShortcuts(AppEntry app, ShortcutTraversalCache shortcutCache)
    {
        if (app.ManageShortcuts)
            shortcutService.RevertShortcuts(app, shortcutCache);
    }
}

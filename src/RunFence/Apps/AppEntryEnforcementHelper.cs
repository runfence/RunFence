using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps;

/// <summary>
/// Encapsulates the per-app enforcement actions (ACL, icon, timestamp, shortcuts, beside-target).
/// Callers are responsible for calling <see cref="IAclService.RecomputeAllAncestorAcls"/> after
/// apply/revert operations, as the appropriate scope varies per call site.
/// </summary>
public class AppEntryEnforcementHelper(
    IAclService aclService,
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IIconService iconService,
    ISidNameCacheService sidNameCache,
    IInteractiveUserDesktopProvider desktopProvider,
    ILoggingService log)
{
    /// <summary>
    /// Applies ACL, creates/updates icon, updates exe timestamp, replaces managed shortcuts,
    /// and creates a beside-target shortcut. Does NOT call RecomputeAllAncestorAcls.
    /// </summary>
    /// <remarks>
    /// For AppContainer apps, beside-target shortcut creation requires an active interactive user
    /// (explorer.exe). When the interactive user is unavailable, the shortcut is skipped and will
    /// be created on the next enforcement cycle.
    /// </remarks>
    public void ApplyChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        ShortcutTraversalCache shortcutCache)
    {
        if (app is { RestrictAcl: true, IsUrlScheme: false })
            aclService.ApplyAcl(app, allApps);

        var iconPath = string.Empty;
        if (app.ManageShortcuts || !app.IsUrlScheme)
            iconPath = iconService.CreateBadgedIcon(app);

        if (app is { IsUrlScheme: false, IsFolder: false } && File.Exists(app.ExePath))
            app.LastKnownExeTimestamp = File.GetLastWriteTimeUtc(app.ExePath);
        else if (app.IsFolder)
            app.LastKnownExeTimestamp = null;

        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (app.ManageShortcuts && File.Exists(launcherPath))
            shortcutService.ReplaceShortcuts(app, launcherPath, iconPath, shortcutCache);

        if (!app.IsUrlScheme && File.Exists(launcherPath))
        {
            // For AppContainer apps, the container runs under the interactive user
            var effectiveSid = app.AppContainerName != null
                ? NativeTokenHelper.TryGetInteractiveUserSid()?.Value
                : app.AccountSid;
            if (effectiveSid != null)
            {
                var username = sidNameCache.GetDisplayName(effectiveSid);
                // Only create the shortcut when a real name was resolved (not just the raw SID)
                if (!string.Equals(username, effectiveSid, StringComparison.OrdinalIgnoreCase))
                    besideTargetShortcutService.CreateBesideTargetShortcut(app, launcherPath, iconPath, username);
            }
            else if (app.AppContainerName != null)
            {
                // Beside-target shortcut skipped because the interactive user (explorer.exe) is not
                // running. The shortcut will be created on the next enforcement cycle.
                log.Warn($"AppEntryEnforcementHelper: interactive user not available, skipping beside-target shortcut for {app.Name}");
            }
        }
    }

    /// <summary>
    /// Creates a shortcut on the interactive user's desktop. No-op if desktop is unavailable
    /// or the app is a URL scheme.
    /// </summary>
    public void CreateDesktopShortcut(AppEntry app)
    {
        if (app.IsUrlScheme)
            return;

        var desktop = desktopProvider.GetDesktopPath();
        if (desktop == null)
            return;

        shortcutService.SaveShortcut(app, Path.Combine(desktop, $"{app.Name}.lnk"));
    }

    /// <summary>
    /// Reverts ACL, managed shortcuts, and beside-target shortcut.
    /// Does NOT call RecomputeAllAncestorAcls.
    /// <paramref name="allApps"/> must include <paramref name="app"/> (RevertAcl filters internally).
    /// </summary>
    public void RevertChanges(AppEntry app, IReadOnlyList<AppEntry> allApps, ShortcutTraversalCache shortcutCache)
    {
        if (app is { RestrictAcl: true, IsUrlScheme: false })
            aclService.RevertAcl(app, allApps);
        if (app.ManageShortcuts)
            shortcutService.RevertShortcuts(app, shortcutCache);
        if (!app.IsUrlScheme)
            besideTargetShortcutService.RemoveBesideTargetShortcut(app);
    }
}

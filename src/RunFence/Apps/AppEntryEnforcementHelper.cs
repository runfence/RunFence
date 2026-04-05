using RunFence.Acl;
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
    IIconService iconService,
    ISidResolver sidResolver,
    IInteractiveUserDesktopProvider desktopProvider)
{
    /// <summary>
    /// Applies ACL, creates/updates icon, updates exe timestamp, replaces managed shortcuts,
    /// and creates a beside-target shortcut. Does NOT call RecomputeAllAncestorAcls.
    /// </summary>
    public void ApplyChanges(
        AppEntry app,
        IReadOnlyList<AppEntry> allApps,
        IReadOnlyDictionary<string, string> sidNames)
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
            shortcutService.ReplaceShortcuts(app, launcherPath, iconPath);

        if (!app.IsUrlScheme && File.Exists(launcherPath))
        {
            // For AppContainer apps, the container runs under the interactive user
            var effectiveSid = app.AppContainerName != null
                ? NativeTokenHelper.TryGetInteractiveUserSid()?.Value
                : app.AccountSid;
            var username = effectiveSid != null
                ? SidNameResolver.ResolveUsername(effectiveSid, sidResolver, sidNames)
                : null;
            if (!string.IsNullOrEmpty(username))
                shortcutService.CreateBesideTargetShortcut(app, launcherPath, iconPath, username);
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
    public void RevertChanges(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app is { RestrictAcl: true, IsUrlScheme: false })
            aclService.RevertAcl(app, allApps);
        if (app.ManageShortcuts)
            shortcutService.RevertShortcuts(app);
        if (!app.IsUrlScheme)
            shortcutService.RemoveBesideTargetShortcut(app);
    }
}
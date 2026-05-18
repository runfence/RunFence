using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// Handles shortcut creation and update operations in the RunAs flow.
/// Extracted from <see cref="RunAsAppEntryManager"/> to separate shortcut concerns.
/// </summary>
public class RunAsAppShortcutCreator(
    IIconService iconService,
    ISidNameCacheService sidNameCache,
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    ISessionProvider sessionProvider,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    IRunFenceLauncherPathProvider launcherPathProvider,
    ILoggingService log)
{
    /// <summary>
    /// Creates a beside-target shortcut for the app, using a badged icon and the interactive
    /// or credential-based user display name. Silently skips if the launcher exe is not found
    /// or the user name cannot be resolved to a display name distinct from the raw SID.
    /// Logs when an AppContainer shortcut is skipped because the interactive SID is unavailable.
    /// </summary>
    public void CreateBesideTargetShortcut(AppEntry app)
    {
        var session = sessionProvider.GetSession();
        var iconPath = iconService.CreateBadgedIcon(app);
        if (!launcherPathProvider.Exists())
            return;
        var launcherPath = launcherPathProvider.GetLauncherPath();

        string? effectiveSid;
        if (app.AppContainerName != null)
        {
            effectiveSid = interactiveUserSidResolver.GetInteractiveUserSid();
            if (string.IsNullOrEmpty(effectiveSid))
                log.Warn($"RunAsAppShortcutCreator: interactive user SID unavailable; skipping AppContainer beside-target shortcut for '{app.Name}'.");
        }
        else
        {
            var credential = session.CredentialStore.Credentials
                .FirstOrDefault(c => string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));
            if (credential == null)
                return;
            effectiveSid = credential.Sid;
        }

        if (string.IsNullOrEmpty(effectiveSid))
            return;
        var username = sidNameCache.GetDisplayName(effectiveSid);
        // Only create the shortcut when a real name was resolved (not just the raw SID)
        if (!string.Equals(username, effectiveSid, StringComparison.OrdinalIgnoreCase))
            besideTargetShortcutService.CreateBesideTargetShortcut(app, launcherPath, iconPath, username);
    }

    /// <summary>
    /// Removes the beside-target shortcut for the app (used during rollback).
    /// </summary>
    public void RemoveBesideTargetShortcut(AppEntry app)
    {
        try
        {
            besideTargetShortcutService.RemoveBesideTargetShortcut(app);
        }
        catch (Exception ex)
        {
            log.Error("Failed to remove beside-target shortcut for RunAs app", ex);
        }
    }

    /// <summary>
    /// Updates an existing .lnk shortcut to point to the RunFence launcher for the given app ID.
    /// Uses the badged icon if it exists in the icon directory.
    /// </summary>
    public void TryUpdateOriginalShortcut(string originalLnkPath, string appId)
    {
        try
        {
            var launcherPath = launcherPathProvider.GetLauncherPath();
            var iconPath = iconService.GetIconPath(appId);
            shortcutService.UpdateShortcutToLauncher(
                originalLnkPath, appId, launcherPath,
                File.Exists(iconPath) ? iconPath : null);
        }
        catch (Exception ex)
        {
            log.Error("Failed to update shortcut to launcher", ex);
        }
    }
}

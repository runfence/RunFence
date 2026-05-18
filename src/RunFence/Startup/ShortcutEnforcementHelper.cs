using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup;

/// <summary>
/// Encapsulates shortcut enforcement phases for startup: managed shortcuts via <see cref="IShortcutService"/>
/// and beside-target shortcuts via <see cref="IBesideTargetShortcutService"/>.
/// These are a distinct concern (filesystem traversal + .lnk manipulation) from ACL enforcement
/// (NTFS security descriptors) handled by <see cref="StartupEnforcementService"/>.
/// </summary>
public class ShortcutEnforcementHelper(
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IIconService iconService,
    SidDisplayNameResolver displayNameResolver,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    ILoggingService log)
{
    private static string LauncherPath => Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);

    /// <summary>
    /// Enforces managed shortcuts for all apps in <paramref name="database"/> that have
    /// <c>ManageShortcuts = true</c>. A null <paramref name="cache"/> indicates no shortcut scan
    /// was prepared (e.g. no apps need shortcuts or launcher is absent) and is a no-op.
    /// </summary>
    public string? EnforceShortcuts(AppDatabase database, ShortcutTraversalCache? cache)
    {
        if (cache == null)
            return null;

        var launcherPath = LauncherPath;
        if (!File.Exists(launcherPath))
            return null;

        try
        {
            shortcutService.EnforceShortcuts(database.Apps, launcherPath, cache);
            return null;
        }
        catch (ShortcutEnforcementException ex)
        {
            log.Warn($"Shortcut enforcement warning: {ex.Message}");
            return ex.Message;
        }
        catch (Exception ex)
        {
            log.Error("Shortcut enforcement failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Enforces beside-target shortcuts for all non-URL apps in <paramref name="database"/>.
    /// Is a no-op when the RunFence Launcher is not found.
    /// </summary>
    public void EnforceBesideTargetShortcuts(AppDatabase database)
    {
        var launcherPath = LauncherPath;
        if (!File.Exists(launcherPath))
            return;

        try
        {
            besideTargetShortcutService.EnforceBesideTargetShortcuts(
                database.Apps.Where(a => !a.IsUrlScheme), launcherPath,
                app =>
                {
                    var effectiveSid =
                        app.AppContainerName != null ? interactiveUserSidResolver.GetInteractiveUserSid() : app.AccountSid;

                    if (app.AppContainerName != null && string.IsNullOrEmpty(effectiveSid))
                        log.Warn($"ShortcutEnforcementHelper: interactive user SID unavailable; skipping AppContainer beside-target shortcut for '{app.Name}'.");
                    if (string.IsNullOrEmpty(effectiveSid))
                        return null;
                    var username = displayNameResolver.ResolveUsername(effectiveSid, database.SidNames);
                    if (string.IsNullOrEmpty(username))
                        return null;
                    var iconPath = iconService.GetIconPath(app.Id);
                    return (username, iconPath);
                });
        }
        catch (Exception ex)
        {
            log.Error("Beside-target shortcut enforcement failed", ex);
        }
    }
}

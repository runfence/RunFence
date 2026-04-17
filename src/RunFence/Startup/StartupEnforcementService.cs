using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Startup;

public class StartupEnforcementService(
    IAclService aclService,
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IShortcutDiscoveryService shortcutDiscovery,
    IIconService iconService,
    ILoggingService log,
    SidDisplayNameResolver displayNameResolver,
    IAppContainerService appContainerService)
    : IStartupEnforcementService
{
    public EnforcementResult Enforce(AppDatabase database)
    {
        log.Info("Starting startup enforcement...");

        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Error($"Launcher not found at: {launcherPath}");
        }

        var timestampUpdates = new Dictionary<string, DateTime>();
        var shortcutApps = new List<AppEntry>();
        var traverseGrants = new List<ContainerTraverseGrant>();

        foreach (var app in database.Apps)
        {
            try
            {
                AppContainerEntry? containerEntry = null;
                // Ensure AppContainer profile exists at startup
                if (app.AppContainerName != null)
                {
                    try
                    {
                        containerEntry = database.AppContainers.FirstOrDefault(c =>
                            string.Equals(c.Name, app.AppContainerName, StringComparison.OrdinalIgnoreCase));
                        if (containerEntry != null)
                            appContainerService.EnsureProfile(containerEntry);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"EnsureProfile failed for AppContainer '{app.AppContainerName}': {ex.Message}");
                    }
                }

                // Target existence is not checked — admin may be denied read access on a path
                // whose ACL is being enforced; skipping in that case would defeat the purpose.

                if (app is { RestrictAcl: true, IsUrlScheme: false })
                {
                    try
                    {
                        aclService.ApplyAcl(app, database.Apps);

                        // After granting the container SID ReadAndExecute on the ACL target,
                        // also ensure traverse ACEs on all ancestor directories so the container
                        // token can reach the target through paths with broken NTFS inheritance.
                        // NTFS ACLs are applied here; traverse grants are collected for re-tracking
                        // on the live database by the caller via ApplyEnforcementResult.
                        if (containerEntry != null)
                        {
                            var containerSid = containerEntry.Sid;
                            if (string.IsNullOrEmpty(containerSid))
                            {
                                log.Warn($"Skipping container traverse enforcement for '{containerEntry.Name}': SID not resolved");
                            }
                            else
                            {
                                var targetPath = aclService.ResolveAclTargetPath(app);
                                var traverseDir = Directory.Exists(targetPath)
                                    ? targetPath
                                    : Path.GetDirectoryName(targetPath);
                                if (traverseDir != null)
                                {
                                    var (_, appliedPaths) = appContainerService.EnsureTraverseAccess(
                                        containerEntry, traverseDir);
                                    traverseGrants.Add(new ContainerTraverseGrant(containerEntry, traverseDir, appliedPaths));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"ACL enforcement failed for {app.Name}", ex);
                    }
                }

                if (!app.IsUrlScheme && iconService.NeedsRegeneration(app))
                {
                    try
                    {
                        iconService.CreateBadgedIcon(app);
                        if (!app.IsFolder && File.Exists(app.ExePath))
                            timestampUpdates[app.Id] = File.GetLastWriteTimeUtc(app.ExePath);
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Icon regeneration failed for {app.Name}", ex);
                    }
                }

                if (app.ManageShortcuts)
                    shortcutApps.Add(app);
            }
            catch (Exception ex)
            {
                log.Error($"Enforcement error for app {app.Name}", ex);
            }
        }

        // Recompute ancestor folder ACLs once after all per-app ACLs are applied
        try
        {
            aclService.RecomputeAllAncestorAcls(database.Apps);
        }
        catch (Exception ex)
        {
            log.Error("Ancestor ACL recomputation failed", ex);
        }

        // Batched shortcut enforcement — single directory scan for all apps
        if (shortcutApps.Count > 0 && File.Exists(launcherPath))
        {
            try
            {
                var shortcutCache = shortcutDiscovery.CreateTraversalCache();
                shortcutService.EnforceShortcuts(shortcutApps, launcherPath, shortcutCache);
            }
            catch (Exception ex)
            {
                log.Error("Shortcut enforcement failed", ex);
            }
        }

        // Beside-target shortcut enforcement for all non-URL apps
        if (File.Exists(launcherPath))
        {
            try
            {
                besideTargetShortcutService.EnforceBesideTargetShortcuts(
                    database.Apps.Where(a => !a.IsUrlScheme), launcherPath,
                    app =>
                    {
                        // Container apps run under the interactive user
                        var effectiveSid =
                            app.AppContainerName != null ? NativeTokenHelper.TryGetInteractiveUserSid()?.Value : app.AccountSid;

                        if (string.IsNullOrEmpty(effectiveSid))
                            return null;
                        var username = displayNameResolver.ResolveUsername(effectiveSid, database.SidNames);
                        if (string.IsNullOrEmpty(username))
                            return null;
                        var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{app.Id}.ico");
                        return (username, iconPath);
                    });
            }
            catch (Exception ex)
            {
                log.Error("Beside-target shortcut enforcement failed", ex);
            }
        }

        log.Info("Startup enforcement completed.");
        return new EnforcementResult(timestampUpdates, traverseGrants);
    }
}

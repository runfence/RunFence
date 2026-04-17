using RunFence.Account.OrphanedProfiles;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;

namespace RunFence.SidMigration;

public class SidDeletionHandler(
    IAclService aclService,
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IOrphanedProfileService orphanedProfileService,
    IFirewallCleanupService firewallCleanupService,
    IPathGrantService pathGrantService,
    ISidMigrationService sidMigrationService,
    ILoggingService log,
    UiThreadDatabaseAccessor dbAccessor)
{
    public void Apply(
        IReadOnlyList<string> sidsToDelete,
        AppDatabase snapshot,
        CredentialStore credentialStore,
        ShortcutTraversalCache shortcutCache,
        List<string> messages)
    {
        foreach (var sid in sidsToDelete)
        {
            var affectedApps = snapshot.Apps
                .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var app in affectedApps)
            {
                try
                {
                    if (app.RestrictAcl)
                        aclService.RevertAcl(app, snapshot.Apps);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert ACL for {app.Name}: {ex.Message}");
                }

                try
                {
                    if (app.ManageShortcuts)
                        shortcutService.RevertShortcuts(app, shortcutCache);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert shortcuts for {app.Name}: {ex.Message}");
                }

                try
                {
                    besideTargetShortcutService.RemoveBesideTargetShortcut(app);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to remove shortcut for {app.Name}: {ex.Message}");
                }
            }

            try
            {
                orphanedProfileService.CleanupLogonScripts(sid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to cleanup logon scripts for {sid}: {ex.Message}");
            }
        }

        foreach (var sid in sidsToDelete)
        {
            try
            {
                firewallCleanupService.RemoveAllRules(sid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for SID '{sid}': {ex.Message}");
            }
        }

        foreach (var sid in sidsToDelete)
        {
            if (snapshot.GetAccount(sid)?.Grants is { Count: > 0 })
            {
                try
                {
                    pathGrantService.RemoveAll(sid, updateFileSystem: true);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert grants for SID '{sid}': {ex.Message}");
                }
            }
        }

        int deletedCreds = 0, deletedApps = 0, deletedCallers = 0;
        dbAccessor.Write(_ =>
        {
            var (creds, apps, callers) =
                sidMigrationService.DeleteSidsFromAppData(sidsToDelete, credentialStore);
            deletedCreds = creds;
            deletedApps = apps;
            deletedCallers = callers;
        });

        var postDeleteSnapshot = dbAccessor.CreateSnapshot();
        aclService.RecomputeAllAncestorAcls(postDeleteSnapshot.Apps);

        messages.Add($"Deleted {deletedCreds} credential(s), {deletedApps} app(s), {deletedCallers} IPC caller(s).");
    }
}

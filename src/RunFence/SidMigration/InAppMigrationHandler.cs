using RunFence.Account.OrphanedProfiles;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class InAppMigrationHandler(
    ISidMigrationService sidMigrationService,
    IAppConfigService appConfigService,
    ILoggingService log,
    IAclService aclService,
    IShortcutService shortcutService,
    AppEntryEnforcementHelper enforcementHelper,
    IOrphanedProfileService orphanedProfileService,
    IGrantedPathAclService grantedPathAcl,
    IFirewallService firewallService)
{
    /// <summary>
    /// Validates that no SID appears in both migration targets and the delete list.
    /// Returns an error message if validation fails, null otherwise.
    /// </summary>
    public string? Validate(IReadOnlyList<SidMigrationMapping> mappings, IReadOnlyList<string> sidsToDelete)
    {
        var migrateTargets = mappings.Select(m => m.NewSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = sidsToDelete.Where(s => migrateTargets.Contains(s)).ToList();
        if (overlap.Count > 0)
            return "Cannot delete SIDs that are also migration targets.";
        return null;
    }

    /// <summary>
    /// Applies in-app SID migration and deletion. Returns messages describing what was done,
    /// a success flag, and an optional save error message.
    /// </summary>
    public (List<string> messages, bool success, string? saveError) Apply(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        try
        {
            var messages = new List<string>();

            if (mappings.Count > 0)
                ApplyMigrations(mappings, session, messages);

            if (sidsToDelete.Count > 0)
                ApplyDeletions(sidsToDelete, session, messages);

            try
            {
                using var scope = session.PinDerivedKey.Unprotect();
                appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, scope.Data);
                return (messages, true, null);
            }
            catch (Exception saveEx)
            {
                log.Error("In-app migration save failed", saveEx);
                return (messages, true, saveEx.Message);
            }
        }
        catch (Exception ex)
        {
            log.Error("In-app migration failed", ex);
            return ([$"Failed: {ex.Message}"], false, null);
        }
    }

    private void ApplyMigrations(
        IReadOnlyList<SidMigrationMapping> mappings,
        SessionContext session,
        List<string> messages)
    {
        var counts = sidMigrationService.MigrateAppData(mappings, session.CredentialStore);
        messages.Add($"Migrated {counts.Credentials} credential(s), {counts.Apps} app(s), " +
                     $"{counts.IpcCallers} IPC caller(s), {counts.AllowEntries} allow entry/entries.");

        // Re-apply firewall rules: remove under old SID, apply under new SID
        foreach (var mapping in mappings)
        {
            try
            {
                firewallService.RemoveAllRules(mapping.OldSid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for old SID '{mapping.OldSid}': {ex.Message}");
            }

            var newSettings = session.Database.GetAccount(mapping.NewSid)?.Firewall;
            if (newSettings is { IsDefault: false })
            {
                var newUsername = session.Database.SidNames.GetValueOrDefault(mapping.NewSid) ?? mapping.Username;
                try
                {
                    firewallService.ApplyFirewallRules(mapping.NewSid, newUsername, newSettings);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to apply firewall rules for new SID '{mapping.NewSid}': {ex.Message}");
                }
            }
        }

        var migratedSids = mappings.Select(m => m.NewSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in session.Database.Apps)
        {
            if (!migratedSids.Contains(app.AccountSid))
                continue;
            try
            {
                enforcementHelper.RevertChanges(app, session.Database.Apps);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert enforcement for {app.Name}: {ex.Message}");
            }

            try
            {
                enforcementHelper.ApplyChanges(app, session.Database.Apps, session.Database.SidNames);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to apply enforcement for {app.Name}: {ex.Message}");
            }
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(session.Database.Apps);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to recompute ancestor ACLs: {ex.Message}");
        }
    }

    private void ApplyDeletions(
        IReadOnlyList<string> sidsToDelete,
        SessionContext session,
        List<string> messages)
    {
        foreach (var sid in sidsToDelete)
        {
            var affectedApps = session.Database.Apps
                .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var app in affectedApps)
            {
                try
                {
                    if (app.RestrictAcl)
                        aclService.RevertAcl(app, session.Database.Apps);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert ACL for {app.Name}: {ex.Message}");
                }

                try
                {
                    if (app.ManageShortcuts)
                        shortcutService.RevertShortcuts(app);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert shortcuts for {app.Name}: {ex.Message}");
                }

                try
                {
                    shortcutService.RemoveBesideTargetShortcut(app);
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

        // Remove firewall rules for deleted SIDs
        foreach (var sid in sidsToDelete)
        {
            try
            {
                firewallService.RemoveAllRules(sid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for SID '{sid}': {ex.Message}");
            }
        }

        // Revert filesystem grants before removing database entries
        foreach (var sid in sidsToDelete)
        {
            var grants = session.Database.GetAccount(sid)?.Grants;
            if (grants is { Count: > 0 })
            {
                try
                {
                    grantedPathAcl.RevertAllGrantsBatch(grants, sid);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revert grants for SID '{sid}': {ex.Message}");
                }
            }
        }

        var (deletedCreds, deletedApps, deletedCallers) =
            sidMigrationService.DeleteSidsFromAppData(sidsToDelete, session.CredentialStore);

        aclService.RecomputeAllAncestorAcls(session.Database.Apps);

        messages.Add($"Deleted {deletedCreds} credential(s), {deletedApps} app(s), {deletedCallers} IPC caller(s).");
    }
}
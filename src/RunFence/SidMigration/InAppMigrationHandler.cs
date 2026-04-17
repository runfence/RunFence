using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class InAppMigrationHandler(
    ISidMigrationService sidMigrationService,
    IAppConfigService appConfigService,
    ILoggingService log,
    IAclService aclService,
    IShortcutDiscoveryService shortcutDiscovery,
    AppEntryEnforcementHelper enforcementHelper,
    IFirewallCleanupService firewallCleanupService,
    IFirewallEnforcementOrchestrator firewallEnforcementOrchestrator,
    SidDeletionHandler sidDeletionHandler,
    UiThreadDatabaseAccessor dbAccessor)
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
    /// Applies in-app SID migration and deletion. Heavy IO (ACL reverts, shortcut cleanup,
    /// firewall removal) runs on a background thread via <see cref="Task.Run"/>. All
    /// <see cref="AppDatabase"/> and <see cref="CredentialStore"/> mutations are marshaled to the
    /// UI thread via <see cref="UiThreadDatabaseAccessor"/>. Returns messages describing what was
    /// done, a success flag, and an optional save error message.
    /// </summary>
    public async Task<(List<string> messages, bool success, string? saveError)> ApplyAsync(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        try
        {
            var messages = new List<string>();

            await Task.Run(() =>
            {
                if (mappings.Count > 0)
                    ApplyMigrations(mappings, session, messages);

                if (sidsToDelete.Count > 0)
                    ApplyDeletions(sidsToDelete, session, messages);

                try
                {
                    var snapshot = dbAccessor.CreateSnapshot();
                    firewallEnforcementOrchestrator.EnforceAll(snapshot);
                }
                catch (Exception ex)
                {
                    log.Warn($"Firewall enforcement after SID migration failed: {ex.Message}");
                }
            });

            var saveError = SaveAfterMigration(session);
            return (messages, true, saveError);
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
        MigrationCounts counts = default;
        dbAccessor.Write(_ => { counts = sidMigrationService.MigrateAppData(mappings, session.CredentialStore); });
        messages.Add($"Migrated {counts.Credentials} credential(s), {counts.Apps} app(s), " +
                     $"{counts.IpcCallers} IPC caller(s), {counts.AllowEntries} allow entry/entries.");

        // Remove firewall rules under the old SID; EnforceAll is run after all SID changes.
        foreach (var mapping in mappings)
        {
            try
            {
                firewallCleanupService.RemoveAllRules(mapping.OldSid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for old SID '{mapping.OldSid}': {ex.Message}");
            }
        }

        var migratedSids = mappings.Select(m => m.NewSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshot = dbAccessor.CreateSnapshot();
        var migratedApps = snapshot.Apps
            .Where(app => migratedSids.Contains(app.AccountSid))
            .ToList();
        var shortcutCache = CreateShortcutCacheIfNeeded(migratedApps);
        foreach (var app in migratedApps)
        {
            try
            {
                enforcementHelper.RevertChanges(app, snapshot.Apps, shortcutCache);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert enforcement for {app.Name}: {ex.Message}");
            }

            try
            {
                enforcementHelper.ApplyChanges(app, snapshot.Apps, shortcutCache);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to apply enforcement for {app.Name}: {ex.Message}");
            }
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(snapshot.Apps);
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
        var snapshot = dbAccessor.CreateSnapshot();
        var affectedApps = sidsToDelete
            .SelectMany(sid => snapshot.Apps
                .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase)));
        var shortcutCache = CreateShortcutCacheIfNeeded(affectedApps);
        sidDeletionHandler.Apply(sidsToDelete, snapshot, session.CredentialStore, shortcutCache, messages);
    }

    private string? SaveAfterMigration(SessionContext session)
    {
        try
        {
            using var scope = session.PinDerivedKey.Unprotect();
            appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, scope.Data);
            return null;
        }
        catch (Exception saveEx)
        {
            log.Error("In-app migration save failed", saveEx);
            return saveEx.Message;
        }
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(IEnumerable<AppEntry> apps)
        => apps.Any(a => a.ManageShortcuts)
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);
}

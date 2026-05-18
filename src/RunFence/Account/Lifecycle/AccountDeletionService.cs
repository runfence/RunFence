using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.SidMigration;

namespace RunFence.Account.Lifecycle;

public class AccountDeletionService(
    IAccountLifecycleManager lifecycleManager,
    IAccountCredentialManager credentialManager,
    IPathGrantService pathGrantService,
    IFirewallCleanupService firewallCleanupService,
    IGlobalIcmpSettingsApplier globalIcmpSettingsApplier,
    ISidCleanupHelper sidCleanup,
    ILoggingService log,
    IAclService aclService,
    ILocalUserProvider localUserProvider,
    IDatabaseProvider databaseProvider) : IAccountDeletionService
{
    public async Task<AccountDeletionCleanupResult> DeleteAccountAsync(string sid, string username,
        CredentialStore credentialStore,
        bool removeApps = true)
    {
        var database = databaseProvider.GetDatabase();
        var deleteResult = lifecycleManager.DeleteSamAccount(sid);
        if (!deleteResult.Succeeded)
            throw new InvalidOperationException(deleteResult.ErrorMessage ?? $"Failed to delete account {sid}");

        // After the SAM account is gone, profile deletion is warning-only cleanup. Await it here
        // so callers do not return early while the deletion is still running in the background.
        await TryDeleteProfileAsync(sid);

        // Intentionally after DeleteUser: lifting sandbox restrictions before deletion creates a security window where the account could execute unrestricted operations
        try
        {
            lifecycleManager.ClearAccountRestrictions(sid, username, database.Settings);
        }
        catch (Exception ex)
        {
            log.Warn($"ClearAccountRestrictions failed for {sid}: {ex.Message}");
        }

        try
        {
            firewallCleanupService.RemoveAllRules(sid);
        }
        catch (Exception ex)
        {
            log.Warn($"RemoveAllRules failed for {sid}: {ex.Message}");
        }

        credentialManager.RemoveCredentialsBySid(sid, credentialStore);

        var cleanupWarnings = new List<string>();
        if (database.GetAccount(sid)?.Grants is { Count: > 0 })
        {
            var grantRemovalResult = pathGrantService.RemoveAll(sid);
            cleanupWarnings.AddRange(grantRemovalResult.Warnings.Select(GrantApplyFailureFormatter.Format));
        }

        // Revert filesystem ACEs before database cleanup (AllowedAclEntries must still be intact).
        var (appsExcludingDeleted, allowModeAppsToReapply) = RevertDeletedAccountAcls(sid, database, aclService);

        sidCleanup.CleanupSidFromAppData(sid, removeApps);

        try
        {
            globalIcmpSettingsApplier.ApplyGlobalIcmpSetting(database);
        }
        catch (Exception ex)
        {
            log.Warn($"EnforceGlobalIcmpBlock failed after deletion of {sid}: {ex.Message}");
        }

        // Reapply allow-mode apps that lost the deleted SID entry, and recompute ancestor ACLs.
        ReapplyAndRecomputeAcls(appsExcludingDeleted, allowModeAppsToReapply, aclService);
        // AccountEntry (including ephemeral flag) is removed by CleanupSidFromAppData above.
        return new AccountDeletionCleanupResult(cleanupWarnings);
    }

    private async Task TryDeleteProfileAsync(string sid)
    {
        try
        {
            var error = await lifecycleManager.DeleteProfileAsync(sid);
            if (!string.IsNullOrEmpty(error))
                log.Warn($"DeleteProfileAsync failed for {sid}: {error}");
        }
        catch (Exception ex)
        {
            log.Warn($"DeleteProfileAsync failed for {sid}: {ex.Message}");
        }
    }

    /// <summary>
    /// Phase 1: Reverts filesystem ACEs for every RestrictAcl app belonging to the deleted account.
    /// Passing appsExcludingDeleted to RevertAcl ensures deny recomputation for shared paths does not
    /// treat the deleted SID as an "allowed" entry ("do not reapply ACL for missing accounts").
    /// Also collects allow-mode apps of other accounts that must be reapplied after database cleanup.
    /// </summary>
    private (List<AppEntry> AppsExcludingDeleted, List<AppEntry> AllowModeAppsToReapply)
        RevertDeletedAccountAcls(string sid, AppDatabase database, IAclService acl)
    {
        // Apps belonging to OTHER accounts — used as the "remaining apps" view throughout,
        // so the deleted account's apps are never included in allowedSids or ancestor recomputation.
        var appsExcludingDeleted = database.Apps
            .Where(a => !SidComparer.SidEquals(a.AccountSid, sid))
            .ToList();

        // Allow-mode apps of OTHER accounts that granted access to the deleted SID.
        // Must be collected now while AllowedAclEntries still contains the deleted SID.
        var allowModeAppsToReapply = appsExcludingDeleted
            .Where(a => a is { RestrictAcl: true, IsUrlScheme: false, AclMode: AclMode.Allow } &&
                        a.AllowedAclEntries?.Any(e =>
                            SidComparer.SidEquals(e.Sid, sid)) == true)
            .ToList();

        // Invalidate so the deleted account is absent from GetLocalUserAccounts().
        localUserProvider.InvalidateCache();

        foreach (var app in database.Apps)
        {
            if (!app.RestrictAcl || app.IsUrlScheme)
                continue;
            if (!SidComparer.SidEquals(app.AccountSid, sid))
                continue;

            try
            {
                acl.RevertAcl(app, appsExcludingDeleted);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert ACL for app '{app.Name}' on account deletion: {ex.Message}");
            }
        }

        return (appsExcludingDeleted, allowModeAppsToReapply);
    }

    /// <summary>
    /// Phase 2 (after CleanupSidFromAppData): reapplies allow-mode apps that lost the deleted SID
    /// entry so the stale filesystem ACE is removed, then recomputes ancestor folder ACLs.
    /// </summary>
    private void ReapplyAndRecomputeAcls(List<AppEntry> appsExcludingDeleted,
        List<AppEntry> allowModeAppsToReapply, IAclService acl)
    {
        // AllowedAclEntries no longer contains the deleted SID after CleanupSidFromAppData,
        // so ApplyAcl produces the correct allow ACEs (the stale ACE is removed).
        foreach (var app in allowModeAppsToReapply)
        {
            try
            {
                acl.ApplyAcl(app, appsExcludingDeleted);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to re-apply allow ACL for app '{app.Name}' after account deletion: {ex.Message}");
            }
        }

        try
        {
            acl.RecomputeAllAncestorAcls(appsExcludingDeleted);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to recompute ancestor ACLs after account deletion: {ex.Message}");
        }
    }
}

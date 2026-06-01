using RunFence.Acl;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account;

public class AccountToggleService(
    IAccountLoginRestrictionService accountRestriction,
    IGroupPolicyScriptHelper groupPolicyScriptHelper,
    ILoggingService log,
    ILicenseService licenseService,
    IAccountFirewallToggle firewallToggle,
    ISessionProvider sessionProvider,
    IGrantSyncService grantSyncService,
    IGrantMutatorService grantMutatorService,
    ITraverseService traverseService) : IAccountToggleService
{
    public SetLogonBlockedResult SetLogonBlocked(string sid, string username, bool blocked)
    {
        var session = sessionProvider.GetSession();
        var database = session.Database;
        if (blocked)
        {
            var hiddenCount = session.CredentialStore.Credentials
                .Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
            if (!licenseService.CanHideAccount(hiddenCount))
                return new SetLogonBlockedResult(false,
                    licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, hiddenCount),
                    IsLicenseLimit: true);
        }

        bool previousScriptBlocked;
        bool previousHiddenState;
        try
        {
            previousScriptBlocked = groupPolicyScriptHelper.IsLoginBlocked(sid);
            previousHiddenState = accountRestriction.GetAccountHiddenStateOrThrow(username);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to capture current logon state for {username}", ex);
            return new SetLogonBlockedResult(false, ex.Message);
        }
        try
        {
            var result = accountRestriction.SetLoginBlockedBySid(sid, username, blocked);
            try
            {
                UpdateLogonGrantTracking(database, sid, result, blocked);
                return new SetLogonBlockedResult(true, null);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to sync logon grant tracking for {username}; rolling back", ex);
                try
                {
                    RestoreLogonState(sid, username, previousScriptBlocked, previousHiddenState);
                }
                catch (Exception rollbackEx)
                {
                    log.Error($"Failed to roll back logon state for {username}", rollbackEx);
                return new SetLogonBlockedResult(
                    false,
                    $"{ex.Message} Rollback failed: {rollbackEx.Message}",
                    FailureStatus: AccountRestrictionStatus.Failed,
                    RollbackAttempted: true);
            }

            return new SetLogonBlockedResult(
                false,
                ex.Message,
                FailureStatus: AccountRestrictionStatus.RolledBack,
                RollbackAttempted: true);
        }
        }
        catch (AccountRestrictionOperationException ex)
        {
            log.Error($"Failed to set logon for {username}", ex);
            return new SetLogonBlockedResult(
                false,
                ex.Message,
                FailureStatus: ex.Status,
                RollbackAttempted: ex.RollbackAttempted);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set logon for {username}", ex);
            return new SetLogonBlockedResult(false, ex.Message);
        }
    }

    public void RestoreLogonState(string sid, string username, bool groupPolicyBlocked, bool hiddenBlocked)
    {
        var database = sessionProvider.GetSession().Database;
        try
        {
            var result = groupPolicyScriptHelper.SetLoginBlocked(sid, groupPolicyBlocked);
            UpdateLogonGrantTracking(database, sid, result, groupPolicyBlocked);
            accountRestriction.RestoreAccountHiddenState(username, hiddenBlocked);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to restore logon state for {username}", ex);
            throw;
        }
    }

    public SetAllowInternetResult SetAllowInternet(string sid, bool allowInternet)
    {
        var database = sessionProvider.GetSession().Database;
        var existing = database.GetAccount(sid)?.Firewall;
        return firewallToggle.SetAllowInternet(sid, allowInternet, existing);
    }

    private void UpdateLogonGrantTracking(AppDatabase database, string sid, SetLoginBlockedResult result, bool blocked)
    {
        if (result.ScriptPath == null)
            return;

        if (blocked)
        {
            // ACEs were already applied by the restriction service/helper; track the grant in DB from NTFS state.
            grantSyncService.UpdateFromPath(result.ScriptPath, sid);
            if (result.TraversePaths is { Count: > 0 })
            {
                var scriptsDir = Path.GetDirectoryName(result.ScriptPath)!;
                var entries = TraversePathsHelper.GetOrCreateTraversePaths(database, sid);
                TraversePathsHelper.TrackPath(entries, scriptsDir, result.TraversePaths, trackedSourceSid: null);
            }

            return;
        }

        // ACEs were already removed by the restriction service/helper; remove DB tracking only.
        var untrackGrantResult = grantMutatorService.UntrackGrant(sid, result.ScriptPath, isDeny: false);
        LogGrantWarnings($"logon grant untrack for SID '{sid}'", untrackGrantResult.Warnings);
        var trackedScriptsDir = Path.GetDirectoryName(result.ScriptPath);
        if (!string.IsNullOrEmpty(trackedScriptsDir))
        {
            // Remove traverse entry for scriptsDir and clean up orphaned ancestor entries.
            var untrackTraverseResult = traverseService.UntrackTraverse(sid, trackedScriptsDir);
            LogGrantWarnings($"logon traverse untrack for SID '{sid}'", untrackTraverseResult.Warnings);
            traverseService.CleanupOrphanedTraverse(sid, trackedScriptsDir);
        }

        database.RemoveAccountIfEmpty(sid);
    }

    private void LogGrantWarnings(string operation, IReadOnlyList<GrantApplyWarning> warnings)
    {
        foreach (var warning in warnings)
            log.Warn($"{operation} completed with warning: {GrantApplyFailureFormatter.Format(warning)}");
    }
}

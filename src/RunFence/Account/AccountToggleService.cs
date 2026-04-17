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
    ILoggingService log,
    ILicenseService licenseService,
    IAccountFirewallSettingsApplier firewallSettingsApplier,
    ISessionProvider sessionProvider,
    IPathGrantService pathGrantService) : IAccountToggleService
{
    public SetLogonBlockedResult SetLogonBlocked(string sid, string username, bool blocked)
    {
        if (blocked)
        {
            var credentialStore = sessionProvider.GetSession().CredentialStore;
            var hiddenCount = credentialStore.Credentials.Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
            if (!licenseService.CanHideAccount(hiddenCount))
                return new SetLogonBlockedResult(false,
                    licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, hiddenCount),
                    IsLicenseLimit: true);
        }

        var database = sessionProvider.GetSession().Database;
        try
        {
            var result = accountRestriction.SetLoginBlockedBySid(sid, username, blocked);
            if (result.ScriptPath != null)
            {
                if (blocked)
                {
                    // ACEs were already applied by accountRestriction — track the grant in DB from NTFS state.
                    pathGrantService.UpdateFromPath(result.ScriptPath, sid);
                    if (result.TraversePaths is { Count: > 0 })
                    {
                        var scriptsDir = Path.GetDirectoryName(result.ScriptPath)!;
                        var entries = TraversePathsHelper.GetOrCreateTraversePaths(database, sid);
                        TraversePathsHelper.TrackPath(entries, scriptsDir, result.TraversePaths);
                    }
                }
                else
                {
                    // ACEs were already removed by accountRestriction — remove DB tracking only.
                    pathGrantService.RemoveGrant(sid, result.ScriptPath, isDeny: false, updateFileSystem: false);
                    var scriptsDir = Path.GetDirectoryName(result.ScriptPath);
                    if (!string.IsNullOrEmpty(scriptsDir))
                    {
                        // Remove traverse entry for scriptsDir and clean up orphaned ancestor entries.
                        pathGrantService.RemoveTraverse(sid, scriptsDir, updateFileSystem: false);
                        pathGrantService.CleanupOrphanedTraverse(sid, scriptsDir);
                    }

                    database.RemoveAccountIfEmpty(sid);
                }
            }

            return new SetLogonBlockedResult(true, null);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set logon for {username}", ex);
            return new SetLogonBlockedResult(false, ex.Message);
        }
    }

    public string? SetAllowInternet(string sid, string username, bool allowInternet)
    {
        var database = sessionProvider.GetSession().Database;
        var settings = database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
        var previousSettings = settings.Clone();
        settings.AllowInternet = allowInternet;
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings);

        var finalSettings = database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
        var resolvedUsername = database.SidNames.GetValueOrDefault(sid) ?? username;
        try
        {
            firewallSettingsApplier.ApplyAccountFirewallSettings(
                sid,
                resolvedUsername,
                previousSettings,
                finalSettings,
                database);
            return null;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            log.Error($"Failed to apply firewall rules for {username}", ex);
            FirewallAccountSettings.UpdateOrRemove(database, sid, previousSettings);
            return ex.CauseMessage;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.GlobalIcmp)
        {
            log.Error($"Failed to enforce global ICMP after applying firewall rules for {username}", ex);
            return ex.CauseMessage;
        }
    }
}

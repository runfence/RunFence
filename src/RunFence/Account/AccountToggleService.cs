using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account;

public class AccountToggleService(
    IAccountRestrictionService accountRestriction,
    ILoggingService log,
    ILicenseService licenseService,
    IFirewallService firewallService,
    ISessionProvider sessionProvider) : IAccountToggleService
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
                    AccountGrantHelper.AddGrant(database, sid, result.ScriptPath);
                    if (result.TraversePaths is { Count: > 0 })
                    {
                        var scriptsDir = Path.GetDirectoryName(result.ScriptPath)!;
                        var entries = TraversePathsHelper.GetOrCreateTraversePaths(database, sid);
                        TraversePathsHelper.TrackPath(entries, scriptsDir, result.TraversePaths);
                    }
                }
                else
                {
                    var scriptNormalized = Path.GetFullPath(result.ScriptPath);
                    var scriptsDir = Path.GetDirectoryName(result.ScriptPath);
                    var grants = database.GetAccount(sid)?.Grants;
                    if (grants != null)
                    {
                        grants.RemoveAll(e => !e.IsTraverseOnly &&
                                              string.Equals(e.Path, scriptNormalized, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(scriptsDir))
                        {
                            var scriptsDirNormalized = Path.GetFullPath(scriptsDir);
                            grants.RemoveAll(e => e.IsTraverseOnly &&
                                                  string.Equals(Path.GetFullPath(e.Path), scriptsDirNormalized,
                                                      StringComparison.OrdinalIgnoreCase));
                        }

                        database.RemoveAccountIfEmpty(sid);
                    }
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
        settings.AllowInternet = allowInternet;
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings);

        var finalSettings = database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
        var resolvedUsername = database.SidNames.GetValueOrDefault(sid) ?? username;
        try
        {
            firewallService.ApplyFirewallRules(sid, resolvedUsername, finalSettings);
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply firewall rules for {username}", ex);
            return ex.Message;
        }
    }
}
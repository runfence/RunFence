using RunFence.Core.Models;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class FirewallEnforcementRetryProcessor(
    IFirewallAccountRuleApplier accountRuleApplier,
    IAuditPolicyService auditPolicyService,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    ILoggingService log)
{
    public void ProcessRetries(AppDatabase database)
    {
        var retriedAccountSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in retryState.GetRetryEntries())
        {
            try
            {
                switch (entry.Layer)
                {
                    case FirewallEnforcementLayer.AuditPolicy:
                    {
                        var result = string.Equals(entry.Key, "enabled", StringComparison.OrdinalIgnoreCase)
                            ? auditPolicyService.EnableBlockedConnectionAuditing()
                            : auditPolicyService.DisableBlockedConnectionAuditing();

                        if (result.Status == AuditPolicyStatus.Succeeded)
                            retryState.MarkRetrySucceeded(FirewallEnforcementLayer.AuditPolicy, entry.Key);
                        else if (result.IsRetryable)
                            retryState.MarkRetryPending(
                                FirewallEnforcementLayer.AuditPolicy,
                                entry.Key,
                                result.Error ?? result.Status.ToString(),
                                "Retry blocked-connection audit policy enforcement.");
                        break;
                    }
                    case FirewallEnforcementLayer.AccountRules:
                    case FirewallEnforcementLayer.WfpFilters:
                    {
                        if (!retriedAccountSids.Add(entry.Key))
                            break;

                        var account = database.GetAccount(entry.Key);
                        if (account == null)
                        {
                            retryState.MarkRetrySucceeded(FirewallEnforcementLayer.AccountRules, entry.Key);
                            retryState.MarkRetrySucceeded(FirewallEnforcementLayer.WfpFilters, entry.Key);
                            break;
                        }

                        var username = database.SidNames.TryGetValue(account.Sid, out var resolvedName)
                            ? resolvedName
                            : account.Sid;
                        var result = accountRuleApplier.ApplyFirewallRules(
                            account.Sid,
                            username,
                            account.Firewall,
                            account.Firewall,
                            domainCache.GetAccountSnapshot(account.Firewall));
                        if (!result.Succeeded)
                            throw new InvalidOperationException(result.ErrorMessage ?? "Firewall retry failed.");
                        retryState.MarkRetrySucceeded(FirewallEnforcementLayer.AccountRules, entry.Key);
                        retryState.MarkRetrySucceeded(FirewallEnforcementLayer.WfpFilters, entry.Key);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                var reason = entry.Layer == FirewallEnforcementLayer.AuditPolicy
                    ? "Retry blocked-connection audit policy enforcement."
                    : "Retry account firewall enforcement.";
                retryState.MarkRetryPending(entry.Layer, entry.Key, ex.Message, reason);
                log.Error($"FirewallEnforcementRetryProcessor: Failed to retry firewall enforcement layer '{entry.Layer}'", ex);
            }
        }
    }
}

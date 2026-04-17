using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallEnforcementOrchestrator(
    ILoggingService log,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    FirewallAccountRuleApplier accountRuleApplier,
    IFirewallCleanupService cleanupService,
    IGlobalIcmpPendingDomainProcessor globalIcmpPendingDomainProcessor,
    IGlobalIcmpEnforcementTrigger globalIcmpEnforcementTrigger)
    : IAccountFirewallSettingsApplier, IFirewallEnforcementOrchestrator
{
    public async Task<FirewallApplyResult> ApplyAccountFirewallSettingsAsync(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database,
        CancellationToken cancellationToken = default)
    {
        var settingsForWorker = settings.Clone();
        var previousSettingsForWorker = previousSettings?.Clone();
        var accountResolvedDomains = domainCache.GetAccountSnapshot(settings);

        FirewallAccountRuleApplyResult accountResult;
        try
        {
            accountResult = await Task.Run(
                () => ApplyFirewallCore(sid, username, settingsForWorker, previousSettingsForWorker, accountResolvedDomains),
                cancellationToken);
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FirewallApplyException(FirewallApplyPhase.AccountRules, sid, ex);
        }

        ProcessAccountResult(accountResult, database);

        var globalIcmpInput = CreateGlobalIcmpWorkerInput(database);
        try
        {
            await globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlockAsync(globalIcmpInput, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new FirewallApplyException(FirewallApplyPhase.GlobalIcmp, sid, ex);
        }

        return new FirewallApplyResult(
            AccountRulesApplied: accountResult.AccountRulesApplied,
            GlobalIcmpApplied: true,
            PendingDomains: accountResult.PendingDomains);
    }

    private static GlobalIcmpPolicyInput CreateGlobalIcmpWorkerInput(AppDatabase database) =>
        new(
            database.Settings.BlockIcmpWhenInternetBlocked,
            database.Accounts
            .Where(account => !account.Firewall.AllowInternet)
            .Select(account => account.Clone())
            .ToList());

    public FirewallApplyResult ApplyAccountFirewallSettings(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database)
    {
        FirewallAccountRuleApplyResult accountResult;
        try
        {
            accountResult = ApplyFirewallCore(sid, username, settings, previousSettings,
                domainCache.GetAccountSnapshot(settings));
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FirewallApplyException(FirewallApplyPhase.AccountRules, sid, ex);
        }

        ProcessAccountResult(accountResult, database);

        try
        {
            globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlock(database);
        }
        catch (Exception ex)
        {
            throw new FirewallApplyException(FirewallApplyPhase.GlobalIcmp, sid, ex);
        }

        return new FirewallApplyResult(
            AccountRulesApplied: accountResult.AccountRulesApplied,
            GlobalIcmpApplied: true,
            PendingDomains: accountResult.PendingDomains);
    }

    private FirewallAccountRuleApplyResult ApplyFirewallCore(
        string sid,
        string username,
        FirewallAccountSettings settings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomains)
        => accountRuleApplier.ApplyFirewallRules(sid, username, settings, previousSettings, resolvedDomains);

    public void EnforceAll(AppDatabase database)
    {
        log.Info("FirewallEnforcementOrchestrator: enforcing all firewall rules.");

        domainCache.Prune(database);
        retryState.Prune(database);

        var activeSids = new HashSet<string>(
            database.Accounts.Where(account => !account.Firewall.IsDefault).Select(account => account.Sid),
            StringComparer.OrdinalIgnoreCase);
        var pendingDomains = new List<FirewallPendingDomainResolution>();
        var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in database.Accounts.Where(account => !account.Firewall.IsDefault))
        {
            try
            {
                var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
                var result = accountRuleApplier.ApplyFirewallRules(
                    account.Sid,
                    username,
                    account.Firewall,
                    account.Firewall,
                    domainCache.GetAccountSnapshot(account.Firewall));
                FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys, result.PendingDomains);
            }
            catch (Exception ex)
            {
                log.Error($"FirewallEnforcementOrchestrator: Failed to apply rules for {account.Sid} during EnforceAll", ex);
            }
        }

        globalIcmpPendingDomainProcessor.ProcessPendingDomains(pendingDomains);

        cleanupService.CleanupOrphanedRules(activeSids, database.SidNames.Keys);

        try
        {
            globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlock(database);
        }
        catch (Exception ex)
        {
            log.Error("FirewallEnforcementOrchestrator: Failed to enforce global ICMP block during EnforceAll", ex);
        }

        log.Info($"FirewallEnforcementOrchestrator: enforcement complete ({activeSids.Count} account(s)).");
    }

    private void ProcessAccountResult(FirewallAccountRuleApplyResult accountResult, AppDatabase database)
    {
        globalIcmpPendingDomainProcessor.ProcessPendingDomains(accountResult.PendingDomains);
        domainCache.Prune(database);
        retryState.Prune(database);
    }
}

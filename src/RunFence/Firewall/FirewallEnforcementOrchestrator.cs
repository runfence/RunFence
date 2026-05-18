using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallEnforcementOrchestrator(
    ILoggingService log,
    FirewallResolvedDomainCache domainCache,
    FirewallEnforcementRetryState retryState,
    IFirewallAccountRuleApplier accountRuleApplier,
    IFirewallCleanupService cleanupService,
    IGlobalIcmpPendingDomainProcessor globalIcmpPendingDomainProcessor,
    IGlobalIcmpEnforcementTrigger globalIcmpEnforcementTrigger,
    FirewallApplyPlanner applyPlanner,
    FirewallApplyRetryCoordinator applyRetryCoordinator,
    FirewallApplyPhaseExecutor applyPhaseExecutor)
    : IAccountFirewallSettingsApplier, IFirewallEnforcementOrchestrator
{
    public async Task<FirewallApplyResult> ApplyAccountFirewallSettingsAsync(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database,
        Action? saveAction = null,
        CancellationToken cancellationToken = default)
    {
        var plan = applyPlanner.BuildApplyPlan(sid, previousSettings, settings);
        var entries = new List<FirewallEnforcementEntry>();
        var pendingDomains = new List<FirewallPendingDomainResolution>();
        var hasSaveAction = saveAction is not null;

        foreach (var phase in plan.Phases)
        {
            var phaseResult = await applyPhaseExecutor.ExecutePhaseAsync(
                phase,
                _ =>
                {
                    saveAction?.Invoke();
                    return Task.CompletedTask;
                },
                database,
                sid,
                username,
                cancellationToken);
            CompletePhase(plan, phaseResult);
            ApplyPhaseOutcome(plan, phaseResult, sid, database, entries, pendingDomains);
            if (!phaseResult.CanContinue)
            {
                return BuildApplyResult(plan, hasSaveAction, pendingDomains, entries);
            }
        }

        return BuildApplyResult(plan, hasSaveAction, pendingDomains, entries);
    }

    public FirewallApplyResult ApplyAccountFirewallSettings(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database,
        Action? saveAction = null)
    {
        var plan = applyPlanner.BuildApplyPlan(sid, previousSettings, settings);
        var entries = new List<FirewallEnforcementEntry>();
        var pendingDomains = new List<FirewallPendingDomainResolution>();
        var hasSaveAction = saveAction is not null;

        foreach (var phase in plan.Phases)
        {
            var phaseResult = applyPhaseExecutor.ExecutePhase(
                phase,
                () =>
                {
                    saveAction?.Invoke();
                },
                database,
                sid,
                username,
                CancellationToken.None);
            CompletePhase(plan, phaseResult);
            ApplyPhaseOutcome(plan, phaseResult, sid, database, entries, pendingDomains);
            if (!phaseResult.CanContinue)
            {
                return BuildApplyResult(plan, hasSaveAction, pendingDomains, entries);
            }
        }

        return BuildApplyResult(plan, hasSaveAction, pendingDomains, entries);
    }

    public EnforceAllResult EnforceAll(AppDatabase database)
    {
        log.Info("FirewallEnforcementOrchestrator: enforcing all firewall rules.");

        var failures = new List<FirewallEnforcementFailure>();

        try
        {
            domainCache.Prune(database);
            retryState.Prune(database);
        }
        catch (Exception ex)
        {
            failures.Add(new FirewallEnforcementFailure(FirewallEnforcementLayer.DnsRefresh, null, ex.Message));
        }

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
                if (!result.Succeeded)
                {
                    applyRetryCoordinator.MarkAccountEnforcementRetryPending(
                        account.Sid,
                        result.ErrorMessage ?? "Firewall account enforcement failed.");
                    failures.Add(new FirewallEnforcementFailure(
                        result.FailedLayer ?? FirewallEnforcementLayer.AccountRules,
                        account.Sid,
                        result.ErrorMessage ?? "Firewall account enforcement failed."));
                    continue;
                }

                FirewallPendingDomainHelper.AddUnique(pendingDomains, pendingKeys, result.PendingDomains);
            }
            catch (Exception ex)
            {
                applyRetryCoordinator.MarkAccountEnforcementRetryPending(account.Sid, ex.Message);
                failures.Add(new FirewallEnforcementFailure(FirewallEnforcementLayer.AccountRules, account.Sid, ex.Message));
            }
        }

        try
        {
            globalIcmpPendingDomainProcessor.ProcessPendingDomains(pendingDomains);
        }
        catch (Exception ex)
        {
            failures.Add(new FirewallEnforcementFailure(FirewallEnforcementLayer.DnsRefresh, null, ex.Message));
        }

        try
        {
            cleanupService.CleanupOrphanedRules(activeSids, database.SidNames.Keys);
        }
        catch (Exception ex)
        {
            failures.Add(new FirewallEnforcementFailure(FirewallEnforcementLayer.AccountRules, null, ex.Message));
        }

        try
        {
            globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlock(database);
        }
        catch (Exception ex)
        {
            applyRetryCoordinator.MarkGlobalIcmpRetryPending(ex.Message);
            failures.Add(new FirewallEnforcementFailure(FirewallEnforcementLayer.GlobalIcmp, null, ex.Message));
        }

        log.Info($"FirewallEnforcementOrchestrator: enforcement complete ({activeSids.Count} account(s)).");
        return new EnforceAllResult(failures);
    }

    private static void CompletePhase(FirewallApplyPlan plan, FirewallApplyPhaseResult phaseResult)
    {
        if (phaseResult.PersistenceCompleted)
            plan.MarkPersistenceCompleted();
    }

    private void ApplyPhaseOutcome(
        FirewallApplyPlan plan,
        FirewallApplyPhaseResult phaseResult,
        string sid,
        AppDatabase database,
        List<FirewallEnforcementEntry> entries,
        List<FirewallPendingDomainResolution> pendingDomains)
    {
        entries.AddRange(phaseResult.Entries);

        if (phaseResult.AccountApplyResult is not null)
        {
            if (!phaseResult.AccountApplyResult.Succeeded)
            {
                if (plan.UpdatesAccountRetryState)
                {
                    applyRetryCoordinator.MarkAccountEnforcementRetryPending(
                        sid,
                        phaseResult.AccountApplyResult.ErrorMessage ?? "Firewall account enforcement failed.");
                }

                return;
            }

            entries.Add(new(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Succeeded));
            entries.Add(new(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Succeeded));
            pendingDomains.AddRange(phaseResult.AccountApplyResult.PendingDomains);
            ProcessAccountResult(phaseResult.AccountApplyResult, database);
        }

        if (phaseResult.GlobalIcmpError is not null)
        {
            if (plan.UpdatesGlobalIcmpRetryState)
                applyRetryCoordinator.MarkGlobalIcmpRetryPending(phaseResult.GlobalIcmpError);

            return;
        }

        if (phaseResult.GlobalIcmpApplied)
            entries.Add(new(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.Succeeded));
    }

    private FirewallApplyResult BuildApplyResult(
        FirewallApplyPlan plan,
        bool hasSaveAction,
        List<FirewallPendingDomainResolution> pendingDomains,
        List<FirewallEnforcementEntry> entries)
    {
        if (plan.RequiresNoOpSuccessEntries)
        {
            entries.Add(new(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Succeeded));
            entries.Add(new(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Succeeded));
        }

        return new FirewallApplyResult(
            ConfigSaved: hasSaveAction && plan.PersistenceAlreadyHappened,
            pendingDomains,
            entries);
    }

    private void ProcessAccountResult(FirewallAccountRuleApplyResult accountResult, AppDatabase database)
    {
        globalIcmpPendingDomainProcessor.ProcessPendingDomains(accountResult.PendingDomains);
        domainCache.Prune(database);
        retryState.Prune(database);
    }
}

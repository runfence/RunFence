using RunFence.Core.Models;
using RunFence.Firewall.Wfp;

namespace RunFence.Firewall;

public class FirewallApplyPhaseExecutor(
    FirewallResolvedDomainCache domainCache,
    IFirewallAccountRuleApplier accountRuleApplier,
    IGlobalIcmpEnforcementTrigger globalIcmpEnforcementTrigger)
{
    public FirewallApplyPhaseResult ExecutePhase(
        FirewallApplyPlanPhase phase,
        Action persistConfig,
        AppDatabase database,
        string sid,
        string username,
        CancellationToken cancellationToken)
    {
        var entries = new List<FirewallEnforcementEntry>();
        var persistenceCompleted = false;
        FirewallAccountRuleApplyResult? accountApplyResult = null;
        var globalIcmpApplied = false;
        string? globalIcmpError = null;

        foreach (var step in EnumerateExecutionSteps(phase))
        {
            switch (step)
            {
                case FirewallPhaseExecutionStep.Persist:
                    PersistSettings(database, sid, phase.TargetSettings, persistConfig);
                    persistenceCompleted = true;
                    break;
                case FirewallPhaseExecutionStep.Account:
                    var accountExecutionResult = ExecuteAccountOperation(phase, username, cancellationToken);
                    accountApplyResult = accountExecutionResult.AccountApplyResult;
                    entries.AddRange(accountExecutionResult.Entries);
                    if (!accountExecutionResult.CanContinue)
                    {
                        return CreatePhaseResult(
                            canContinue: false,
                            persistenceCompleted,
                            accountApplyResult,
                            globalIcmpApplied,
                            globalIcmpError,
                            entries);
                    }

                    break;
                case FirewallPhaseExecutionStep.GlobalIcmp:
                    var globalIcmpExecutionResult = ExecuteGlobalIcmpOperation(phase, database, sid, cancellationToken);
                    globalIcmpApplied = globalIcmpExecutionResult.Applied;
                    globalIcmpError = globalIcmpExecutionResult.Error;
                    entries.AddRange(globalIcmpExecutionResult.Entries);
                    if (!globalIcmpExecutionResult.CanContinue)
                    {
                        return CreatePhaseResult(
                            canContinue: false,
                            persistenceCompleted,
                            accountApplyResult,
                            globalIcmpApplied,
                            globalIcmpError,
                            entries);
                    }

                    break;
            }
        }

        return CreatePhaseResult(
            canContinue: true,
            persistenceCompleted,
            accountApplyResult,
            globalIcmpApplied,
            globalIcmpError,
            entries);
    }

    public async Task<FirewallApplyPhaseResult> ExecutePhaseAsync(
        FirewallApplyPlanPhase phase,
        Func<CancellationToken, Task> persistConfigAsync,
        AppDatabase database,
        string sid,
        string username,
        CancellationToken cancellationToken)
    {
        var entries = new List<FirewallEnforcementEntry>();
        var persistenceCompleted = false;
        FirewallAccountRuleApplyResult? accountApplyResult = null;
        var globalIcmpApplied = false;
        string? globalIcmpError = null;

        foreach (var step in EnumerateExecutionSteps(phase))
        {
            switch (step)
            {
                case FirewallPhaseExecutionStep.Persist:
                    await PersistSettingsAsync(database, sid, phase.TargetSettings, persistConfigAsync, cancellationToken);
                    persistenceCompleted = true;
                    break;
                case FirewallPhaseExecutionStep.Account:
                    var accountExecutionResult = await ExecuteAccountOperationAsync(phase, username, cancellationToken);
                    accountApplyResult = accountExecutionResult.AccountApplyResult;
                    entries.AddRange(accountExecutionResult.Entries);
                    if (!accountExecutionResult.CanContinue)
                    {
                        return CreatePhaseResult(
                            canContinue: false,
                            persistenceCompleted,
                            accountApplyResult,
                            globalIcmpApplied,
                            globalIcmpError,
                            entries);
                    }

                    break;
                case FirewallPhaseExecutionStep.GlobalIcmp:
                    var globalIcmpExecutionResult = await ExecuteGlobalIcmpOperationAsync(phase, database, sid, cancellationToken);
                    globalIcmpApplied = globalIcmpExecutionResult.Applied;
                    globalIcmpError = globalIcmpExecutionResult.Error;
                    entries.AddRange(globalIcmpExecutionResult.Entries);
                    if (!globalIcmpExecutionResult.CanContinue)
                    {
                        return CreatePhaseResult(
                            canContinue: false,
                            persistenceCompleted,
                            accountApplyResult,
                            globalIcmpApplied,
                            globalIcmpError,
                            entries);
                    }

                    break;
            }
        }

        return CreatePhaseResult(
            canContinue: true,
            persistenceCompleted,
            accountApplyResult,
            globalIcmpApplied,
            globalIcmpError,
            entries);
    }

    private AccountOperationExecutionResult ExecuteAccountOperation(
        FirewallApplyPlanPhase phase,
        string username,
        CancellationToken cancellationToken)
    {
        if (phase.AccountOperation is null)
            return new(null, [], true);

        var operation = phase.AccountOperation;
        var accountResult = ExecuteAccountApplyWithFailures(
            operation.AccountSid,
            username,
            operation.RequestedSettings.Clone(),
            operation.PreviousSettings.Clone(),
            domainCache.GetAccountSnapshot(operation.RequestedSettings),
            cancellationToken);
        if (accountResult.Succeeded)
            return new(accountResult, [], true);

        return new(
            accountResult,
            [
                new(
                    accountResult.FailedLayer ?? FirewallEnforcementLayer.AccountRules,
                    FirewallEnforcementStatus.Failed,
                    accountResult.ErrorMessage ?? "Firewall account enforcement failed.")
            ],
            false);
    }

    private async Task<AccountOperationExecutionResult> ExecuteAccountOperationAsync(
        FirewallApplyPlanPhase phase,
        string username,
        CancellationToken cancellationToken)
    {
        if (phase.AccountOperation is null)
            return new(null, [], true);

        var operation = phase.AccountOperation;
        var accountResult = await ExecuteAccountApplyWithFailuresAsync(
            operation.AccountSid,
            username,
            operation.RequestedSettings.Clone(),
            operation.PreviousSettings.Clone(),
            domainCache.GetAccountSnapshot(operation.RequestedSettings),
            cancellationToken);
        if (accountResult.Succeeded)
            return new(accountResult, [], true);

        return new(
            accountResult,
            [
                new(
                    accountResult.FailedLayer ?? FirewallEnforcementLayer.AccountRules,
                    FirewallEnforcementStatus.Failed,
                    accountResult.ErrorMessage ?? "Firewall account enforcement failed.")
            ],
            false);
    }

    private GlobalIcmpOperationExecutionResult ExecuteGlobalIcmpOperation(
        FirewallApplyPlanPhase phase,
        AppDatabase database,
        string sid,
        CancellationToken cancellationToken)
    {
        if (phase.GlobalIcmpOperation is null)
            return new(true, false, null, []);

        try
        {
            globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlock(
                CreateDatabaseWithRequestedSettings(database, sid, phase.GlobalIcmpOperation.RequestedSettings));
            return new(true, true, null, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(
                phase.Kind == FirewallApplyPlanPhaseKind.RemoveOrLoosen,
                false,
                ex.Message,
                [new(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, ex.Message)]);
        }
    }

    private async Task<GlobalIcmpOperationExecutionResult> ExecuteGlobalIcmpOperationAsync(
        FirewallApplyPlanPhase phase,
        AppDatabase database,
        string sid,
        CancellationToken cancellationToken)
    {
        if (phase.GlobalIcmpOperation is null)
            return new(true, false, null, []);

        try
        {
            var globalDatabase = CreateDatabaseWithRequestedSettings(database, sid, phase.GlobalIcmpOperation.RequestedSettings);
            var globalIcmpInput = CreateGlobalIcmpWorkerInput(globalDatabase);
            await globalIcmpEnforcementTrigger.EnforceGlobalIcmpBlockAsync(globalIcmpInput, cancellationToken);
            return new(true, true, null, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(
                phase.Kind == FirewallApplyPlanPhaseKind.RemoveOrLoosen,
                false,
                ex.Message,
                [new(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, ex.Message)]);
        }
    }

    private static FirewallApplyPhaseResult CreatePhaseResult(
        bool canContinue,
        bool persistenceCompleted,
        FirewallAccountRuleApplyResult? accountApplyResult,
        bool globalIcmpApplied,
        string? globalIcmpError,
        IEnumerable<FirewallEnforcementEntry> entries)
        => new(
            canContinue,
            persistenceCompleted,
            accountApplyResult,
            globalIcmpApplied,
            globalIcmpError,
            entries.ToArray());

    private FirewallAccountRuleApplyResult ExecuteAccountApplyWithFailures(
        string sid,
        string username,
        FirewallAccountSettings requestedSettings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomains,
        CancellationToken cancellationToken)
    {
        try
        {
            return ApplyFirewallCore(
                sid,
                username,
                requestedSettings,
                previousSettings,
                resolvedDomains);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildAccountRuleFailureResult(ex);
        }
    }

    private async Task<FirewallAccountRuleApplyResult> ExecuteAccountApplyWithFailuresAsync(
        string sid,
        string username,
        FirewallAccountSettings requestedSettings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomains,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () => ApplyFirewallCore(
                    sid,
                    username,
                    requestedSettings,
                    previousSettings,
                    resolvedDomains),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildAccountRuleFailureResult(ex);
        }
    }

    private FirewallAccountRuleApplyResult ApplyFirewallCore(
        string sid,
        string username,
        FirewallAccountSettings settings,
        FirewallAccountSettings? previousSettings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomains)
        => accountRuleApplier.ApplyFirewallRules(sid, username, settings, previousSettings, resolvedDomains);

    private static FirewallAccountRuleApplyResult BuildAccountRuleFailureResult(Exception exception)
    {
        var failedLayer = exception is WfpFilterHelperException || exception.InnerException is WfpFilterHelperException
            ? FirewallEnforcementLayer.WfpFilters
            : FirewallEnforcementLayer.AccountRules;
        return new FirewallAccountRuleApplyResult(false, [], failedLayer, exception.Message);
    }

    private static void PersistSettings(
        AppDatabase database,
        string sid,
        FirewallAccountSettings settings,
        Action persistConfig)
    {
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings.Clone());
        persistConfig();
    }

    private static async Task PersistSettingsAsync(
        AppDatabase database,
        string sid,
        FirewallAccountSettings settings,
        Func<CancellationToken, Task> persistConfigAsync,
        CancellationToken cancellationToken)
    {
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings.Clone());
        await persistConfigAsync(cancellationToken);
    }

    private static IEnumerable<FirewallPhaseExecutionStep> EnumerateExecutionSteps(FirewallApplyPlanPhase phase)
    {
        if (phase.ShouldPersist && phase.PersistConfigBeforeExecution)
            yield return FirewallPhaseExecutionStep.Persist;

        if (phase.AccountOperation is not null)
            yield return FirewallPhaseExecutionStep.Account;

        if (phase.GlobalIcmpOperation is not null)
            yield return FirewallPhaseExecutionStep.GlobalIcmp;

        if (phase.ShouldPersist && !phase.PersistConfigBeforeExecution)
            yield return FirewallPhaseExecutionStep.Persist;
    }

    private static GlobalIcmpPolicyInput CreateGlobalIcmpWorkerInput(AppDatabase database)
        => new(
            database.Settings.BlockIcmpWhenInternetBlocked,
            database.Accounts
                .Where(account => !account.Firewall.AllowInternet)
                .Select(account => account.Clone())
                .ToList());

    private static AppDatabase CreateDatabaseWithRequestedSettings(AppDatabase database, string sid, FirewallAccountSettings settings)
    {
        var snapshot = database.CreateSnapshot();
        FirewallAccountSettings.UpdateOrRemove(snapshot, sid, settings.Clone());
        return snapshot;
    }

    private enum FirewallPhaseExecutionStep
    {
        Persist,
        Account,
        GlobalIcmp
    }

    private sealed record AccountOperationExecutionResult(
        FirewallAccountRuleApplyResult? AccountApplyResult,
        IReadOnlyList<FirewallEnforcementEntry> Entries,
        bool CanContinue);

    private sealed record GlobalIcmpOperationExecutionResult(
        bool CanContinue,
        bool Applied,
        string? Error,
        IReadOnlyList<FirewallEnforcementEntry> Entries);
}

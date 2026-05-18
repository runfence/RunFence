using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI;

/// <summary>
/// Orchestrates the Apply workflow for <see cref="AclManagerDialog"/> by delegating plan
/// creation, ACL execution, and post-processing to focused collaborators.
/// </summary>
public class AclManagerApplyOrchestrator(
    AclApplyPlanBuilder planBuilder,
    AclApplyExecutor applyExecutor,
    AclApplyPostProcessor postProcessor,
    ISessionSaver sessionSaver,
    ILoggingService log)
{
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private bool _isContainer;

    public bool IsApplyInProgress { get; private set; }

    public void Initialize(
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
    }

    public async Task<AclApplyOutcome> ApplyAsync(
        IProgress<(int current, int total)> progress,
        Action<bool> setApplyEnabled,
        Action<bool> setDialogEnabled,
        Action refreshGrids)
    {
        if (IsApplyInProgress)
            return new AclApplyOutcome(false, [], []);

        var plan = planBuilder.Build(_pending);
        if (!plan.HasWork)
            return new AclApplyOutcome(true, [], []);

        IsApplyInProgress = true;
        setDialogEnabled(false);
        setApplyEnabled(false);
        progress.Report((0, plan.TotalOperations));
        AclApplyOutcome outcome;

        try
        {
            var executionResult = await applyExecutor.ExecuteAsync(plan, _sid, _isContainer, progress);
            outcome = postProcessor.Apply(plan, executionResult, _pending, _sid, _isContainer);
            if (outcome.Succeeded)
            {
                try
                {
                    sessionSaver.SaveConfig();
                }
                catch (Exception ex)
                {
                    log.Error("ACL Manager apply succeeded in memory but failed to save config", ex);
                    var warning = new GrantApplyWarning(
                        GrantApplyFailureStep.PostGrantMutationSave,
                        ResolveRepresentativePath(plan) ?? string.Empty,
                        ConfigPath: null,
                        ex);
                    outcome = new AclApplyOutcome(true, outcome.Errors, [.. outcome.Warnings, warning]);
                }
            }
        }
        finally
        {
            IsApplyInProgress = false;
            setDialogEnabled(true);
            setApplyEnabled(_pending.HasPendingChanges);
            refreshGrids();
        }

        return outcome;
    }

    private static string? ResolveRepresentativePath(AclApplyPlan plan)
        => plan.PendingAdds.Select(entry => entry.Path)
            .Concat(plan.PendingRemoves.Select(entry => entry.Path))
            .Concat(plan.PendingModifications.Select(modification => modification.Entry.Path))
            .Concat(plan.PendingGrantFixes.Select(entry => entry.Path))
            .Concat(plan.PendingTraverseAdds.Select(entry => entry.Path))
            .Concat(plan.PendingTraverseRemoves.Select(entry => entry.Path))
            .Concat(plan.PendingTraverseFixes.Select(entry => entry.Path))
            .Concat(plan.PendingUntrackGrants.Select(entry => entry.Path))
            .Concat(plan.PendingUntrackTraverse.Select(entry => entry.Path))
            .Concat(plan.PendingConfigMoves.Select(move => move.Entry.Path))
            .Concat(plan.PendingTraverseConfigMoves.Select(move => move.Entry.Path))
            .FirstOrDefault();
}

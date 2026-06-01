using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.SidMigration;

public class InAppMigrationHandler(SidMigrationApplicationService appService)
{
    public string? Validate(IReadOnlyList<SidMigrationMapping> mappings, IReadOnlyList<string> sidsToDelete)
    {
        var migrateTargets = mappings.Select(m => m.NewSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = sidsToDelete.Where(s => migrateTargets.Contains(s)).ToList();
        return overlap.Count > 0 ? "Cannot delete SIDs that are also migration targets." : null;
    }

    public async Task<InAppMigrationApplyResult> ApplyAsync(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        var result = await appService.ApplyAsync(mappings, sidsToDelete, session);
        var workflow = result.Workflow;
        if (workflow.Status is SidMigrationWorkflowStatus.Succeeded or SidMigrationWorkflowStatus.AppliedButSaveFailed)
            return new InAppMigrationApplyResult(result.Messages, true, result.SaveError);

        if (workflow.Errors.Count > 0)
            return new InAppMigrationApplyResult(workflow.Errors, false, result.SaveError);

        return new InAppMigrationApplyResult(["Migration failed."], false, result.SaveError);
    }
}

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

    public async Task<(List<string> messages, bool success, string? saveError)> ApplyAsync(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        var (workflow, messages, saveError) = await appService.ApplyAsync(mappings, sidsToDelete, session);
        if (workflow.Status is SidMigrationWorkflowStatus.Succeeded or SidMigrationWorkflowStatus.AppliedButSaveFailed)
            return (messages, true, saveError);

        var failed = workflow.Errors.Count > 0 ? workflow.Errors[0] : "Migration failed.";
        return ([failed], false, saveError);
    }
}

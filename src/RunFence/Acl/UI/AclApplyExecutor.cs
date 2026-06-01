using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public class AclApplyExecutor(
    AclApplyPhaseCatalog phaseCatalog,
    AclApplyPhaseExecutor phaseExecutor)
{
    public async Task<AclApplyExecutionResult> ExecuteAsync(
        AclApplyPlan plan,
        string sid,
        bool isContainer,
        IProgress<(int current, int total)> progress)
    {
        var result = new AclApplyExecutionResult();
        var totalOperations = phaseCatalog.GetTotalOperations(plan);
        var current = 0;
        var grantConfigMoves = plan.PendingConfigMoves.ToDictionary(
            move => (Path.GetFullPath(move.Entry.Path), move.Entry.IsDeny),
            move => move.TargetConfigPath,
            new GrantPathKeyComparer());
        var traverseConfigMoves = plan.PendingTraverseConfigMoves.ToDictionary(
            move => Path.GetFullPath(move.Entry.Path),
            move => move.TargetConfigPath,
            StringComparer.OrdinalIgnoreCase);
        var context = new AclApplyPhaseExecutionContext(
            plan,
            sid,
            grantConfigMoves,
            traverseConfigMoves,
            result,
            progress,
            totalOperations);

        foreach (var descriptor in phaseCatalog.OrderedPhases)
        {
            current = await phaseExecutor.ExecutePhaseAsync(descriptor, context, current);

            if (result.WasCanceled || result.HasFatalFailure)
                return result;
        }

        return result;
    }
}

using RunFence.Persistence;

namespace RunFence.Acl;

public class GrantAccountCleanupService(
    PersistedGrantMutationWorkflow persistedGrantMutationWorkflow,
    PersistedTraverseMutationWorkflow persistedTraverseMutationWorkflow,
    IGrantIntentStoreSaveService grantIntentStoreSaveService) : IGrantAccountCleanupService
{
    public GrantApplyResult RemoveAll(string accountSid)
    {
        var grantMutation = persistedGrantMutationWorkflow.RemoveAllGrantsWithoutSaving(accountSid);
        var traverseMutation = persistedTraverseMutationWorkflow.RemoveAllTraverseWithoutSaving(
            accountSid,
            grantMutation.AffectedStores);
        if (!grantMutation.Result.DatabaseModified && !traverseMutation.Result.DatabaseModified)
            return default;

        var savePath = grantMutation.Result.DatabaseModified
            ? grantMutation.SavePath
            : traverseMutation.SavePath;
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            GetCombinedStores(grantMutation.AffectedStores, traverseMutation.AffectedStores),
            GrantApplyFailureStep.PostRemoveAllSave,
            savePath);

        return new GrantApplyResult(
            GrantApplied: grantMutation.Result.GrantApplied,
            TraverseApplied: traverseMutation.Result.TraverseApplied,
            DatabaseModified: grantMutation.Result.DatabaseModified || traverseMutation.Result.DatabaseModified,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    public GrantApplyResult UntrackAll(string accountSid)
    {
        var grantMutation = persistedGrantMutationWorkflow.UntrackAllGrantsWithoutSaving(accountSid);
        var traverseMutation = persistedTraverseMutationWorkflow.UntrackAllTraverseWithoutSaving(accountSid);
        if (!grantMutation.Result.DatabaseModified && !traverseMutation.Result.DatabaseModified)
            return default;

        var savePath = grantMutation.Result.DatabaseModified
            ? grantMutation.SavePath
            : traverseMutation.SavePath;
        var warnings = grantIntentStoreSaveService.SaveWithWarnings(
            GetCombinedStores(grantMutation.AffectedStores, traverseMutation.AffectedStores),
            GrantApplyFailureStep.UntrackAllSave,
            savePath);

        return new GrantApplyResult(
            DatabaseModified: grantMutation.Result.DatabaseModified || traverseMutation.Result.DatabaseModified,
            DurableSaveCompleted: warnings.Count == 0,
            Warnings: warnings);
    }

    private static IReadOnlyList<IGrantIntentStore> GetCombinedStores(
        IReadOnlyList<IGrantIntentStore>? grantStores,
        IReadOnlyList<IGrantIntentStore>? traverseStores)
        => (grantStores ?? [])
            .Concat(traverseStores ?? [])
            .Distinct()
            .ToList();
}

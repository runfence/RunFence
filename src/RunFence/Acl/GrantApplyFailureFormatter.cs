namespace RunFence.Acl;

public static class GrantApplyFailureFormatter
{
    private static readonly Dictionary<GrantApplyFailureStep, string> _stepDescriptions = new()
    {
        { GrantApplyFailureStep.GrantIntentSave, "Failed to save the grant intent" },
        { GrantApplyFailureStep.TraverseIntentSave, "Failed to save the traverse grant intent" },
        { GrantApplyFailureStep.GrantAclApply, "Failed to apply target ACL" },
        { GrantApplyFailureStep.TraverseAclApply, "Failed to apply traverse ACL" },
        { GrantApplyFailureStep.TargetEffectiveAccessValidation, "Failed to validate target effective access" },
        { GrantApplyFailureStep.TraverseEffectiveAccessValidation, "Failed to validate traverse effective access" },
        { GrantApplyFailureStep.GrantAclRemove, "Failed to remove target ACL" },
        { GrantApplyFailureStep.TraverseAclRemove, "Failed to remove traverse ACL" },
        { GrantApplyFailureStep.DenyConflictGrantAclRemove, "Failed to remove deny-conflict target ACL" },
        { GrantApplyFailureStep.DenyConflictGrantAclApply, "Failed to apply deny-conflict target ACL" },
        { GrantApplyFailureStep.DenyConflictPostRemoveSave, "Failed to save deny-conflict state after target ACL removal" },
        { GrantApplyFailureStep.DenyConflictPostUpdateSave, "Failed to save deny-conflict state after target ACL update" },
        { GrantApplyFailureStep.DenyConflictRollback, "Failed to rollback deny-conflict change" },
        { GrantApplyFailureStep.RemoveAllGrantAclRemove, "Failed to remove all target ACLs" },
        { GrantApplyFailureStep.RemoveAllTraverseAclRemove, "Failed to remove all traverse ACLs" },
        { GrantApplyFailureStep.PostGrantMutationSave, "Failed to save state after grant mutation" },
        { GrantApplyFailureStep.PostGrantRemoveSave, "Failed to save state after target ACL removal" },
        { GrantApplyFailureStep.PostTraverseRemoveSave, "Failed to save state after traverse ACL removal" },
        { GrantApplyFailureStep.PostRemoveAllSave, "Failed to save state after complete grant removal" },
        { GrantApplyFailureStep.UntrackGrantSave, "Failed to save grant untrack state" },
        { GrantApplyFailureStep.UntrackTraverseSave, "Failed to save traverse untrack state" },
        { GrantApplyFailureStep.UntrackAllSave, "Failed to save untrack-all state" },
        { GrantApplyFailureStep.RevertIntentSave, "Failed to save revert intent" },
        { GrantApplyFailureStep.GrantAclRollback, "Failed to rollback target ACL" },
        { GrantApplyFailureStep.TraverseAclRollback, "Failed to rollback traverse ACL" },
        { GrantApplyFailureStep.FixGrantAclApply, "Failed to re-apply fixed target ACL" },
        { GrantApplyFailureStep.FixTraverseAclApply, "Failed to re-apply fixed traverse ACL" }
    };

    public static string DescribeStep(GrantApplyFailureStep step)
    {
        if (_stepDescriptions.TryGetValue(step, out var description))
            return description;

        return $"Failure in {step}";
    }

    public static bool IsSaveFailureStep(GrantApplyFailureStep step)
        => step is GrantApplyFailureStep.GrantIntentSave
            or GrantApplyFailureStep.TraverseIntentSave
            or GrantApplyFailureStep.DenyConflictPostRemoveSave
            or GrantApplyFailureStep.DenyConflictPostUpdateSave
            or GrantApplyFailureStep.PostGrantMutationSave
            or GrantApplyFailureStep.PostGrantRemoveSave
            or GrantApplyFailureStep.PostTraverseRemoveSave
            or GrantApplyFailureStep.PostRemoveAllSave
            or GrantApplyFailureStep.UntrackGrantSave
            or GrantApplyFailureStep.UntrackTraverseSave
            or GrantApplyFailureStep.UntrackAllSave
            or GrantApplyFailureStep.RevertIntentSave;

    public static string Format(GrantApplyFailure failure)
        => Format(failure.Step, failure.Path, failure.ConfigPath, failure.Exception);

    public static string Format(GrantApplyWarning warning)
        => Format(warning.Step, warning.Path, warning.ConfigPath, warning.Cause);

    public static string Format(GrantApplyFailureStep step, string? path, string? configPath, Exception cause)
    {
        var pathText = string.IsNullOrWhiteSpace(path) ? "<unknown path>" : $"'{path}'";
        var configText = configPath is null ? "main config" : $"'{configPath}'";

        return $"{DescribeStep(step)} for target {pathText} in {configText}: {cause.Message}";
    }
}

namespace RunFence.Acl;

public enum GrantApplyFailureStep
{
    GrantIntentSave,
    TraverseIntentSave,
    GrantAclApply,
    TraverseAclApply,
    TargetEffectiveAccessValidation,
    TraverseEffectiveAccessValidation,
    GrantAclRemove,
    TraverseAclRemove,
    DenyConflictGrantAclRemove,
    DenyConflictGrantAclApply,
    DenyConflictPostRemoveSave,
    DenyConflictPostUpdateSave,
    DenyConflictRollback,
    RemoveAllGrantAclRemove,
    RemoveAllTraverseAclRemove,
    PostGrantMutationSave,
    PostGrantRemoveSave,
    PostTraverseRemoveSave,
    PostRemoveAllSave,
    UntrackGrantSave,
    UntrackTraverseSave,
    UntrackAllSave,
    RevertIntentSave,
    GrantAclRollback,
    TraverseAclRollback,
    FixGrantAclApply,
    FixTraverseAclApply
}

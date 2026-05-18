namespace RunFence.Acl;

public readonly record struct GrantApplyWarning(
    GrantApplyFailureStep Step,
    string Path,
    string? ConfigPath,
    Exception Cause);

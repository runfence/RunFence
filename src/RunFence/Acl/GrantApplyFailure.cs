namespace RunFence.Acl;

public readonly record struct GrantApplyFailure(
    GrantApplyFailureStep Step,
    string? Path,
    string? ConfigPath,
    Exception Exception)
{
    public override string ToString() => GrantApplyFailureFormatter.Format(this);
}

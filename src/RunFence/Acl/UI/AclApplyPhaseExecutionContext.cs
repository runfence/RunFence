namespace RunFence.Acl.UI;

public sealed record AclApplyPhaseExecutionContext(
    AclApplyPlan Plan,
    string Sid,
    IReadOnlyDictionary<(string Path, bool IsDeny), string?> GrantConfigMoves,
    IReadOnlyDictionary<string, string?> TraverseConfigMoves,
    AclApplyExecutionResult Result,
    IProgress<(int current, int total)> Progress,
    int Total);

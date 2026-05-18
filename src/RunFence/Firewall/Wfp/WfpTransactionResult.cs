namespace RunFence.Firewall.Wfp;

public sealed record WfpTransactionResult(
    bool Committed,
    string? Error = null,
    Exception? FailureException = null);

namespace RunFence.Groups.UI;

public enum GroupRefreshRetryOperation
{
    Refresh,
    Reconcile
}

public record GroupRefreshRetryState(
    IReadOnlyList<string> FailedSids,
    GroupRefreshRetryOperation Operation,
    string ErrorText,
    DateTime LastAttemptUtc);

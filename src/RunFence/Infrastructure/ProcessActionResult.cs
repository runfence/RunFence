namespace RunFence.Infrastructure;

public enum ProcessActionStatus
{
    Succeeded,
    StaleProcess,
    AccessDenied,
    Failed
}

public readonly record struct ProcessActionResult(
    ProcessActionStatus Status,
    string? ErrorMessage = null)
{
    public static ProcessActionResult Success() => new(ProcessActionStatus.Succeeded);
    public static ProcessActionResult Stale() => new(ProcessActionStatus.StaleProcess);
    public static ProcessActionResult Denied(string? errorMessage = null) => new(ProcessActionStatus.AccessDenied, errorMessage);
    public static ProcessActionResult Failure(string? errorMessage) => new(ProcessActionStatus.Failed, errorMessage);
}

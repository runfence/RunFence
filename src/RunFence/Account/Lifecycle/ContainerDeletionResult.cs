namespace RunFence.Account.Lifecycle;

public sealed record ContainerDeletionResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings)
{
    public static ContainerDeletionResult Success(IReadOnlyList<string>? warnings = null)
        => new(true, null, warnings ?? []);

    public static ContainerDeletionResult Failure(string errorMessage, IReadOnlyList<string>? warnings = null)
        => new(false, errorMessage, warnings ?? []);
}

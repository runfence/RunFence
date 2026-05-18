namespace RunFence.Apps;

public sealed record AssociationAutoSetResult(
    AssociationAutoSetStatus Status,
    IReadOnlyList<string> WarningMessages)
{
    public static AssociationAutoSetResult Success { get; } =
        new(AssociationAutoSetStatus.Succeeded, []);

    public bool HasWarnings => WarningMessages.Count > 0;
}

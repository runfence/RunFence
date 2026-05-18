namespace RunFence.Account.Lifecycle;

public sealed record EphemeralContainerProcessingResult(
    bool Changed,
    IReadOnlyList<string> Warnings)
{
    public static EphemeralContainerProcessingResult Create(bool changed, IReadOnlyList<string>? warnings = null)
        => new(changed, warnings ?? []);
}

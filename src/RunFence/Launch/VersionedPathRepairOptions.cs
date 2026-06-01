namespace RunFence.Launch;

public readonly record struct VersionedPathRepairOptions(IReadOnlyList<string> UnversionedBoundaryPaths)
{
    public static VersionedPathRepairOptions Empty { get; } = new([]);
}

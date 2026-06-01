namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationChangeSet
{
    public required IReadOnlyList<FolderHandlerRegistryValueSnapshot> ValueSnapshots { get; init; }
    public required IReadOnlyList<string> CreatedKeyPaths { get; init; }
}

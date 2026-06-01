namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationMaintenanceResult
{
    public bool RegistryChanged { get; init; }
    public bool RunOnceChanged { get; init; }
    public bool HadOwnedRegistrationBeforeCall { get; init; }
    public FolderHandlerRegistrationChangeSet? ChangeSet { get; init; }
}

namespace RunFence.Apps;

public readonly record struct HandlerCommandTarget(
    string ResolvedPath,
    HandlerCommandTargetRegistryScope Scope,
    string AssociationKey,
    string? CommandText,
    string? DefaultIconRawValue,
    string? ResolvedDefaultIconPath)
{
    public bool HasExplicitDefaultIcon { get; init; }
}

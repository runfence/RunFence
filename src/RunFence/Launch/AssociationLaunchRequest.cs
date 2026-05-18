namespace RunFence.Launch;

public sealed record AssociationLaunchRequest(
    string AssociationKey,
    string? RawArgument,
    string? NormalizedTarget);

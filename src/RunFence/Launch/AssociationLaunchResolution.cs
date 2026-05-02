using RunFence.Core.Models;

namespace RunFence.Launch;

public sealed record AssociationLaunchResolution(
    AssociationLaunchResolutionStatus Status,
    AppEntry? App = null,
    HandlerMappingEntry? Entry = null);

public enum AssociationLaunchResolutionStatus
{
    Success,
    UnknownAssociation,
    AppNotFound,
    AccessDenied,
    PathPrefixMismatch
}

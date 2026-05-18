namespace RunFence.Core.Helpers;

public enum AssociationFallbackRegistryStatus
{
    Succeeded,
    NoFallbackRecorded,
    AccessDenied,
    Failed
}

public enum AssociationFallbackShellNotifyStatus
{
    Succeeded,
    NotRequired,
    Failed
}

public sealed record AssociationFallbackRestoreResult(
    AssociationFallbackRegistryStatus RegistryStatus,
    AssociationFallbackShellNotifyStatus ShellNotifyStatus,
    string? FallbackValue,
    string? Warning,
    string? Error);

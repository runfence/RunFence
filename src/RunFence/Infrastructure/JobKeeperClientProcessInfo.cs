using System.Security.Principal;

namespace RunFence.Infrastructure;

public readonly record struct JobKeeperClientProcessInfo(
    string? ImagePath,
    SecurityIdentifier? OwnerSid,
    int? IntegrityLevel);

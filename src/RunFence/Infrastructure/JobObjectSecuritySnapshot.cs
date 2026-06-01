using System.Security.Principal;

namespace RunFence.Infrastructure;

public sealed record JobObjectSecuritySnapshot(
    SecurityIdentifier? Owner,
    bool HasDiscretionaryAcl,
    IReadOnlyList<JobObjectAccessEntry> AccessEntries);

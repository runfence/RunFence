using System.Security.Principal;

namespace RunFence.Infrastructure;

public sealed record JobObjectAccessEntry(
    SecurityIdentifier Identity,
    int AccessMask,
    bool IsAllow);

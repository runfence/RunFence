using RunFence.Core;

namespace RunFence.Infrastructure;

public class InteractiveUserSidResolver : IInteractiveUserSidResolver
{
    public string? GetInteractiveUserSid() => SidResolutionHelper.GetInteractiveUserSid();
}

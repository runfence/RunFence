using RunFence.Core;

namespace RunFence.Startup;

public sealed class InteractiveUserSidCache : IInteractiveUserSidCache
{
    public void ReinitializeInteractiveUserSid() => SidResolutionHelper.ReinitializeInteractiveUserSid();
}

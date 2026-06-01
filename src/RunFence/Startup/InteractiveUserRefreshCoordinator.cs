using RunFence.Infrastructure;

namespace RunFence.Startup;

public sealed class InteractiveUserRefreshCoordinator(
    IInteractiveUserSidCache interactiveUserSidCache,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider)
{
    public event Action? InteractiveUserRefreshed;

    public void Refresh()
    {
        interactiveUserSidCache.ReinitializeInteractiveUserSid();
        interactiveUserDesktopProvider.InvalidateCache();
        InteractiveUserRefreshed?.Invoke();
    }
}

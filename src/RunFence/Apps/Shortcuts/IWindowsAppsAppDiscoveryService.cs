using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IWindowsAppsAppDiscoveryService
{
    IReadOnlyList<DiscoveredApp> DiscoverApps();
}

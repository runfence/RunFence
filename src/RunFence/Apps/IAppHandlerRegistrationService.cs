using RunFence.Core.Models;

namespace RunFence.Apps;

public interface IAppHandlerRegistrationService
{
    /// <summary>
    /// Registers all associations in <paramref name="effectiveHandlerMappings"/> to the interactive user's
    /// registry hive. Creates per-association ProgIds, updates shared Capabilities, and removes stale ProgIds
    /// from prior runs. Skips entries whose appId is not found in <paramref name="apps"/>.
    /// </summary>
    void Sync(Dictionary<string, string> effectiveHandlerMappings, List<AppEntry> apps);

    /// <summary>
    /// Removes all RunFence handler ProgIds, the shared Capabilities parent key, and the
    /// RegisteredApplications entry from the interactive user's registry hive.
    /// </summary>
    void UnregisterAll();
}
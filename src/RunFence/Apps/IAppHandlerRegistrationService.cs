using RunFence.Core.Models;

namespace RunFence.Apps;

public interface IAppHandlerRegistrationService
{
    /// <summary>
    /// Registers all associations in <paramref name="effectiveHandlerMappings"/> to HKLM\Software\Classes.
    /// Creates per-association ProgIds, updates shared Capabilities, and removes stale ProgIds
    /// from prior runs. Skips entries whose appId is not found in <paramref name="apps"/>.
    /// </summary>
    void Sync(Dictionary<string, HandlerMappingEntry> effectiveHandlerMappings, List<AppEntry> apps);

    /// <summary>
    /// Removes all RunFence handler ProgIds, the shared Capabilities parent key, and the
    /// RegisteredApplications entry from HKLM\Software\Classes.
    /// Per-user HKCU cleanup is handled by <see cref="IAssociationAutoSetService"/>.
    /// </summary>
    void UnregisterAll();
}

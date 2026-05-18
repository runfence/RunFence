using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Provides mutation-related persistence and refresh operations for application CRUD flows.
/// </summary>
public interface IApplicationMutationContext
{
    AppDatabase Database { get; }

    void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false);

    void RefreshAfterInMemoryMutation(string? selectAppId = null, int fallbackIndex = -1);
}

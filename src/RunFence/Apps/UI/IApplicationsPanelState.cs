using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Provides read-only state from <see cref="Forms.ApplicationsPanel"/> to
/// <see cref="ApplicationsGridPopulator"/> without exposing the full panel.
/// </summary>
public interface IApplicationsPanelState
{
    AppDatabase Database { get; }
    CredentialStore CredentialStore { get; }
    bool IsSortActive { get; }
    int SortColumnIndex { get; }
}
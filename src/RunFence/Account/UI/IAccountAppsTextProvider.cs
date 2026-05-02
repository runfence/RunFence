using RunFence.Core.Models;

namespace RunFence.Account.UI;

/// <summary>
/// Provides the apps text for an account, used in grid sorting.
/// </summary>
public interface IAccountAppsTextProvider
{
    string GetAppsText(AppDatabase database, string? sid);
}

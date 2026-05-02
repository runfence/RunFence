using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles config save operations for <see cref="Forms.ApplicationsPanel"/>,
/// encapsulating key derivation and the targeted vs. full-save decision.
/// Grid refresh and selection after save remain the panel's responsibility.
/// </summary>
public class ApplicationsPanelSaveHelper(IAppConfigService appConfigService, ISessionProvider sessionProvider)
{
    /// <summary>
    /// Saves all config files for the current session's database.
    /// </summary>
    public void SaveAll()
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveAllConfigs(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }

    /// <summary>
    /// Saves only the config file that contains the given app.
    /// </summary>
    public void SaveForApp(string appId)
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveConfigForApp(appId, session.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }
}

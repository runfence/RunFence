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
        appConfigService.SaveAllConfigs(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
    }

    /// <summary>
    /// Saves only the config file that contains the given app.
    /// </summary>
    public void SaveForApp(string appId)
    {
        var session = sessionProvider.GetSession();
        appConfigService.SaveConfigForApp(appId, session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
    }
}

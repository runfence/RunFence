using RunFence.Account;
using RunFence.Infrastructure;
using RunFence.Wizard;

namespace RunFence.Startup;

/// <summary>
/// Implements <see cref="IWizardSessionSaver"/> — saves session state and notifies that data changed.
/// </summary>
public class WizardSessionSaverAdapter(
    IAccountCredentialManager credentialManager,
    ISessionProvider sessionProvider,
    IDataChangeNotifier dataChangeNotifier) : IWizardSessionSaver
{
    public void SaveAndRefresh()
    {
        var session = sessionProvider.GetSession();
        credentialManager.SaveCredentialStoreAndConfig(
            session.CredentialStore, session.Database, session.PinDerivedKey);
        dataChangeNotifier.NotifyDataChanged();
    }
}
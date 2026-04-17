using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Toggles tray-pin flags (folder browser, discovery, terminal) and manage-associations flag for account entries.
/// </summary>
public class AccountTrayToggleService(
    SessionPersistenceHelper persistenceHelper,
    ISessionProvider sessionProvider,
    IAssociationAutoSetService associationAutoSetService)
{
    public void ToggleFolderBrowserTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(session.Database, accountSid, a => a.TrayFolderBrowser, (a, v) => a.TrayFolderBrowser = v, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public void ToggleDiscoveryTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(session.Database, accountSid, a => a.TrayDiscovery, (a, v) => a.TrayDiscovery = v, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public void ToggleTerminalTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(session.Database, accountSid, a => a.TrayTerminal, (a, v) => a.TrayTerminal = v, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public void ToggleManageAssociations(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        var acct = session.Database.GetOrCreateAccount(accountSid);
        var newValue = !acct.ManageAssociations;
        acct.ManageAssociations = newValue;
        session.Database.RemoveAccountIfEmpty(accountSid);
        persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
        if (!newValue)
            associationAutoSetService.RestoreForUser(accountSid);
        else
            associationAutoSetService.AutoSetForUser(accountSid);
        onSaved();
    }

    private void ToggleTrayFlag(AppDatabase db, string sid,
        Func<AccountEntry, bool> getter, Action<AccountEntry, bool> setter,
        CredentialStore store, ProtectedBuffer key, Action onSaved)
    {
        var acct = db.GetOrCreateAccount(sid);
        setter(acct, !getter(acct));
        db.RemoveAccountIfEmpty(sid);
        persistenceHelper.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
    }
}

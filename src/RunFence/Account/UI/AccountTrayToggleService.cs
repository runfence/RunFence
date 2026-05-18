using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;

namespace RunFence.Account.UI;

/// <summary>
/// Toggles tray-pin flags (folder browser, discovery, terminal), manage-associations flag,
/// and receive-injected-input flag for account entries.
/// </summary>
public class AccountTrayToggleService(
    SessionPersistenceHelper persistenceHelper,
    ISessionProvider sessionProvider,
    IAssociationAutoSetService associationAutoSetService,
    IInputInjectionBlockerService injectionBlocker)
{
    public void ToggleFolderBrowserTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(
            session.Database,
            accountSid,
            a => a.TrayFolderBrowser,
            (a, v) => a.TrayFolderBrowser = v,
            session.CredentialStore,
            session.PinDerivedKey,
            onSaved);
    }

    public void ToggleDiscoveryTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(
            session.Database,
            accountSid,
            a => a.TrayDiscovery,
            (a, v) => a.TrayDiscovery = v,
            session.CredentialStore,
            session.PinDerivedKey,
            onSaved);
    }

    public void ToggleTerminalTray(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(
            session.Database,
            accountSid,
            a => a.TrayTerminal,
            (a, v) => a.TrayTerminal = v,
            session.CredentialStore,
            session.PinDerivedKey,
            onSaved);
    }

    public void ToggleManageAssociations(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        var acct = session.Database.GetOrCreateAccount(accountSid);
        var newValue = !acct.ManageAssociations;

        if (newValue)
        {
            acct.ManageAssociations = true;
            session.Database.RemoveAccountIfEmpty(accountSid);
            persistenceHelper.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
            associationAutoSetService.AutoSetForUser(accountSid);
        }
        else
        {
            associationAutoSetService.RestoreForUser(accountSid);
            acct.ManageAssociations = false;
            session.Database.RemoveAccountIfEmpty(accountSid);
            persistenceHelper.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        }

        onSaved();
    }

    public void ToggleReceiveInjectedInput(string accountSid, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        ToggleTrayFlag(
            session.Database,
            accountSid,
            a => a.ReceiveInjectedInput,
            (a, v) => a.ReceiveInjectedInput = v,
            session.CredentialStore,
            session.PinDerivedKey,
            onSaved);
        injectionBlocker.UpdateExemptedSids(
            session.Database.Accounts.Where(a => a.ReceiveInjectedInput).Select(a => a.Sid).ToList());
    }

    private void ToggleTrayFlag(AppDatabase db, string sid,
        Func<AccountEntry, bool> getter, Action<AccountEntry, bool> setter,
        CredentialStore store, ISecureSecretSnapshotSource key, Action onSaved)
    {
        var acct = db.GetOrCreateAccount(sid);
        setter(acct, !getter(acct));
        db.RemoveAccountIfEmpty(sid);
        persistenceHelper.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
    }
}

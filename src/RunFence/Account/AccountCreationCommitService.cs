using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account;

/// <summary>
/// Encapsulates the database-level commit work when creating a new user account:
/// credential store update, SID-name cache update, account entry addition,
/// warning flag persistence, and credential store save.
/// </summary>
public class AccountCreationCommitService(
    IAccountCredentialManager credentialManager,
    ISidNameCacheService sidNameCache,
    ISessionProvider sessionProvider,
    ILocalUserProvider localUserProvider,
    IAssociationAutoSetService autoSetService,
    SessionPersistenceHelper persistenceHelper) : IAccountCreationCommitService
{
    public AccountCreationCommitResult? Commit(EditAccountDialog dialog, AppDatabase database)
    {
        var session = sessionProvider.GetSession();
        var credId = credentialManager.StoreCreatedUserCredential(
            dialog.CreatedSid!, dialog.CreatedPassword!,
            session.CredentialStore, session.PinDerivedKey);

        if (credId == null)
            return null;

        localUserProvider.InvalidateCache();
        sidNameCache.ResolveAndCache(dialog.CreatedSid!, dialog.NewUsername!);

        autoSetService.AutoSetForUser(dialog.CreatedSid!);

        var createdEntry = database.GetOrCreateAccount(dialog.CreatedSid!);
        if (dialog.IsEphemeral)
            createdEntry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

        createdEntry.PrivilegeLevel = dialog.SelectedPrivilegeLevel;

        if (dialog.FirewallSettingsChanged)
        {
            var fwSettings = new FirewallAccountSettings
            {
                AllowInternet = dialog.AllowInternet,
                AllowLocalhost = dialog.AllowLocalhost,
                AllowLan = dialog.AllowLan
            };
            FirewallAccountSettings.UpdateOrRemove(database, dialog.CreatedSid!, fwSettings);
        }

        bool showFirstAccountWarning = !database.Settings.HasShownFirstAccountWarning;
        if (showFirstAccountWarning)
            database.Settings.HasShownFirstAccountWarning = true;

        bool showUsersGroupWarning = dialog.UsersGroupUnchecked && !database.Settings.HasShownUsersGroupWarning;
        if (showUsersGroupWarning)
            database.Settings.HasShownUsersGroupWarning = true;

        persistenceHelper.SaveCredentialStoreAndConfig(session.CredentialStore, database, session.PinDerivedKey);

        return new AccountCreationCommitResult(credId, showFirstAccountWarning, showUsersGroupWarning);
    }
}

using RunFence.Apps;
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
    public AccountCreationCommitResult? Commit(AccountCreationData data, AppDatabase database)
    {
        var session = sessionProvider.GetSession();
        var credId = credentialManager.StoreCreatedUserCredential(
            data.CreatedSid, data.CreatedPassword,
            session.CredentialStore, session.PinDerivedKey);

        if (credId == null)
            return null;

        localUserProvider.InvalidateCache();
        sidNameCache.ResolveAndCache(data.CreatedSid, data.NewUsername);

        autoSetService.AutoSetForUser(data.CreatedSid);

        var createdEntry = database.GetOrCreateAccount(data.CreatedSid);
        if (data.IsEphemeral)
            createdEntry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

        createdEntry.PrivilegeLevel = data.PrivilegeLevel;

        if (data.FirewallSettingsChanged)
        {
            var fwSettings = new FirewallAccountSettings
            {
                AllowInternet = data.AllowInternet,
                AllowLocalhost = data.AllowLocalhost,
                AllowLan = data.AllowLan
            };
            FirewallAccountSettings.UpdateOrRemove(database, data.CreatedSid, fwSettings);
        }

        bool showFirstAccountWarning = !database.Settings.HasShownFirstAccountWarning;
        if (showFirstAccountWarning)
            database.Settings.HasShownFirstAccountWarning = true;

        bool showUsersGroupWarning = data.UsersGroupUnchecked && !data.AdminGroupChecked && !database.Settings.HasShownUsersGroupWarning;
        if (showUsersGroupWarning)
            database.Settings.HasShownUsersGroupWarning = true;

        persistenceHelper.SaveCredentialStoreAndConfig(session.CredentialStore, database, session.PinDerivedKey);

        return new AccountCreationCommitResult(credId, showFirstAccountWarning, showUsersGroupWarning);
    }
}

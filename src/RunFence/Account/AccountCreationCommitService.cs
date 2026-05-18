using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account;

/// <summary>
/// Encapsulates the database-level commit work when creating a new user account:
/// credential store update, SID-name cache update, account entry addition,
/// warning flag persistence, association auto-set, and credential/config save.
/// </summary>
public class AccountCreationCommitService(
    IAccountCredentialManager credentialManager,
    ISidNameCacheService sidNameCache,
    ISessionProvider sessionProvider,
    ILocalUserProvider localUserProvider,
    IAssociationAutoSetService autoSetService,
    SessionPersistenceHelper persistenceHelper) : IAccountCreationCommitService
{
    public AccountCreationCommitOutcome Commit(AccountCreationData data, AppDatabase database)
    {
        var session = sessionProvider.GetSession();
        var creationRollbackState = data.CreationRollbackState;
        var previousSettings = database.Settings.Clone();
        Guid? credId = null;
        AccountCreationCommitResult? result = null;
        AccountCreationRollbackState? rollbackState = null;

        try
        {
            credId = credentialManager.StoreCreatedUserCredential(
                data.CreatedSid,
                data.CreatedPassword,
                session.CredentialStore,
                session.PinDerivedKey);

            if (credId == null)
                return new AccountCreationCommitOutcome(
                    AccountCreationCommitStatus.DuplicateCredential,
                    Result: null,
                    RollbackState: null,
                    ErrorMessage: null);

            localUserProvider.InvalidateCache();
            sidNameCache.ResolveAndCache(data.CreatedSid, data.NewUsername);

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

            bool showUsersGroupWarning =
                data.UsersGroupUnchecked &&
                !data.AdminGroupChecked &&
                !database.Settings.HasShownUsersGroupWarning;
            if (showUsersGroupWarning)
                database.Settings.HasShownUsersGroupWarning = true;

            autoSetService.AutoSetForUser(data.CreatedSid);
            rollbackState = BuildRollbackState(
                data,
                creationRollbackState,
                previousSettings,
                credId);
            result = new AccountCreationCommitResult(credId.Value, showFirstAccountWarning, showUsersGroupWarning);

            persistenceHelper.SaveCredentialStoreAndConfig(
                session.CredentialStore,
                database,
                session.PinDerivedKey);
        }
        catch (Exception ex)
        {
            return new AccountCreationCommitOutcome(
                AccountCreationCommitStatus.SaveFailedAfterMutation,
                result,
                rollbackState ?? BuildRollbackState(
                    data,
                    creationRollbackState,
                    previousSettings,
                    credId),
                ex.Message);
        }

        return new AccountCreationCommitOutcome(
            AccountCreationCommitStatus.Succeeded,
            result,
            RollbackState: null,
            ErrorMessage: null);
    }

    private static AccountCreationRollbackState BuildRollbackState(
        AccountCreationData data,
        CreatedAccountRollbackState? creationRollbackState,
        AppSettings previousSettings,
        Guid? credentialId)
        => new()
        {
            CreatedAccount = new CreatedAccountRollbackState
            {
                Sid = data.CreatedSid,
                Username = data.NewUsername,
                CredentialId = credentialId,
                PreviousAccount = creationRollbackState?.PreviousAccount?.Clone(),
                HadPreviousAccount = creationRollbackState?.HadPreviousAccount == true,
                PreviousSidName = creationRollbackState?.PreviousSidName,
                HadPreviousSidName = creationRollbackState?.HadPreviousSidName == true,
                PreviousFirewallSettings = creationRollbackState?.PreviousFirewallSettings?.Clone(),
                HadPreviousFirewallSettings = creationRollbackState?.HadPreviousFirewallSettings == true
            },
            PreviousSettings = previousSettings
        };
}

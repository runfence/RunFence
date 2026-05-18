using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account;

public sealed class CreatedAccountRollbackExecutor(
    IAccountLifecycleManager lifecycleManager,
    IAccountCredentialManager credentialManager,
    IAssociationAutoSetService associationAutoSetService,
    ILocalUserProvider localUserProvider,
    ILoggingService log)
{
    public async Task RollbackAsync(
        CreatedAccountRollbackState state,
        CredentialStore credentialStore,
        AppDatabase database)
    {
        var deleteResult = lifecycleManager.DeleteSamAccount(state.Sid);
        if (!deleteResult.Succeeded)
        {
            throw new InvalidOperationException(
                deleteResult.ErrorMessage ?? $"Failed to delete account {state.Sid} during rollback.");
        }

        bool profileDeleted;
        try
        {
            var error = await lifecycleManager.DeleteProfileAsync(state.Sid);
            if (string.IsNullOrEmpty(error))
            {
                profileDeleted = true;
            }
            else
            {
                log.Warn($"CreatedAccountRollbackExecutor: DeleteProfileAsync failed for {state.Sid}: {error}");
                profileDeleted = false;
            }
        }
        catch (Exception ex)
        {
            log.Warn($"CreatedAccountRollbackExecutor: DeleteProfileAsync failed for {state.Sid}: {ex.Message}");
            profileDeleted = false;
        }

        if (!profileDeleted)
        {
            try
            {
                associationAutoSetService.RestoreForUser(state.Sid);
            }
            catch (Exception ex)
            {
                log.Warn($"CreatedAccountRollbackExecutor: RestoreForUser failed for {state.Sid}: {ex.Message}");
            }
        }

        try
        {
            lifecycleManager.ClearAccountRestrictions(state.Sid, state.Username, database.Settings);
        }
        catch (Exception ex)
        {
            log.Warn($"CreatedAccountRollbackExecutor: ClearAccountRestrictions failed for {state.Sid}: {ex.Message}");
        }

        if (state.CredentialId != null)
            credentialManager.RemoveCredential(state.CredentialId.Value, credentialStore);

        var existingEntry = database.GetAccount(state.Sid);
        if (existingEntry != null)
            database.Accounts.Remove(existingEntry);

        if (state.HadPreviousAccount && state.PreviousAccount != null)
        {
            var restored = state.PreviousAccount.Clone();
            if (state.HadPreviousFirewallSettings && state.PreviousFirewallSettings != null)
                restored.Firewall = state.PreviousFirewallSettings.Clone();
            else
                restored.Firewall = new FirewallAccountSettings();

            database.Accounts.Add(restored);
        }

        if (state.HadPreviousSidName && state.PreviousSidName != null)
            database.SidNames[state.Sid] = state.PreviousSidName;
        else
            database.SidNames.Remove(state.Sid);

        localUserProvider.InvalidateCache();
    }
}

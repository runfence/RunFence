using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

public class AccountCreationRollbackService(
    CreatedAccountRollbackExecutor rollbackExecutor)
{
    public async Task RollbackAsync(
        AccountCreationRollbackState state,
        AppDatabase database,
        CredentialStore credentialStore)
    {
        await rollbackExecutor.RollbackAsync(state.CreatedAccount, credentialStore, database);
        database.Settings = state.PreviousSettings.Clone();
    }
}

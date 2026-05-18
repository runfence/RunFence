using RunFence.Core.Models;

namespace RunFence.Account;

public interface IAccountCreationCommitService
{
    AccountCreationCommitOutcome Commit(AccountCreationData data, AppDatabase database);
}

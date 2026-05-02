using RunFence.Core.Models;

namespace RunFence.Account;

public interface IAccountCreationCommitService
{
    AccountCreationCommitResult? Commit(AccountCreationData data, AppDatabase database);
}

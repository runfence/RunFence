using RunFence.Core.Models;

namespace RunFence.Account;

public sealed class AccountCreationRollbackState
{
    public required CreatedAccountRollbackState CreatedAccount { get; init; }
    public required AppSettings PreviousSettings { get; init; }
}

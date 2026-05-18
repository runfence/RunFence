using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;

namespace RunFence.RunAs;

public sealed record RunAsCreatedAccountPersistenceRequest
{
    public string? CreatedSid { get; init; }
    public string? Username { get; init; }
    public ProtectedString? CreatedPassword { get; init; }
    public CreatedAccountRollbackState? CreatedRollbackState { get; init; }
    public CreateAccountStatus CreatedAccountStatus { get; init; }
    public string? CreatedAccountErrorMessage { get; init; }
    public bool ScheduleEphemeralCleanupOnRollbackFailure { get; init; }
}

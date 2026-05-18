using RunFence.Core;

namespace RunFence.Account.UI;

public sealed record CreateAccountResult(
    CreateAccountStatus Status,
    string Sid,
    ProtectedString? Password,
    string Username,
    bool IsEphemeral,
    List<string> Errors,
    string? ErrorMessage = null,
    CreatedAccountRollbackState? RollbackState = null,
    IReadOnlyList<AccountRestrictionEntry>? RestrictionEntries = null);

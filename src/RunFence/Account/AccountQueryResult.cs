using RunFence.Core.Models;

namespace RunFence.Account;

public sealed record AccountQueryResult(
    AccountQueryStatus Status,
    LocalUserAccount? Account,
    string? ProfilePath,
    string? InteractiveUserSid,
    bool? IsInteractiveUser,
    string? Error);

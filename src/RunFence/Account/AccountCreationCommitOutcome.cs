namespace RunFence.Account;

public enum AccountCreationCommitStatus
{
    Succeeded,
    DuplicateCredential,
    SaveFailedAfterMutation
}

public sealed record AccountCreationCommitOutcome(
    AccountCreationCommitStatus Status,
    AccountCreationCommitResult? Result,
    AccountCreationRollbackState? RollbackState,
    string? ErrorMessage);

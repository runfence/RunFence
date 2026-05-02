namespace RunFence.Account;

public record AccountCreationCommitResult(
    Guid? CredId,
    bool ShowFirstAccountWarning,
    bool ShowUsersGroupWarning);

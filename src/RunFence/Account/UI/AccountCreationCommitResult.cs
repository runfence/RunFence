namespace RunFence.Account.UI;

public record AccountCreationCommitResult(
    Guid? CredId,
    bool ShowFirstAccountWarning,
    bool ShowUsersGroupWarning);

namespace RunFence.Account;

public enum CredentialLookupStatus
{
    Success,
    CurrentAccount,
    InteractiveUser,
    NotFound,
    MissingPassword
}

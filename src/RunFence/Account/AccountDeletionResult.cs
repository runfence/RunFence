namespace RunFence.Account;

public sealed record AccountDeletionResult(
    bool Succeeded,
    string Sid,
    string? ErrorMessage)
{
    public void Deconstruct(out bool success, out string? errorMessage)
    {
        success = Succeeded;
        errorMessage = ErrorMessage;
    }

}

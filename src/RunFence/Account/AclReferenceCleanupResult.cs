namespace RunFence.Account;

public sealed record AclReferenceCleanupResult(
    int FixedCount,
    string? ErrorMessage)
{
    public void Deconstruct(out int fixedCount, out string? errorMessage)
    {
        fixedCount = FixedCount;
        errorMessage = ErrorMessage;
    }

}

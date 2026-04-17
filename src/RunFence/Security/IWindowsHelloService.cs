namespace RunFence.Security;

public enum HelloVerificationResult
{
    Verified,
    Canceled,
    NotAvailable,
    Failed
}

public interface IWindowsHelloService
{
    Task<bool> IsAvailableAsync();
    Task<HelloVerificationResult> VerifyAsync(string message);
}
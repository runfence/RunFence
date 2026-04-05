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
    HelloVerificationResult VerifySync(string message);
}
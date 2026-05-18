using RunFence.Security;

namespace RunFence.Startup.UI;

public interface ICredentialUnlockService
{
    CredentialUnlockResult VerifyPin();
    Task<CredentialUnlockResult> VerifyAsync(CredentialUnlockMode mode);
}

using RunFence.Core.Models;

namespace RunFence.Startup.UI;

public interface IAccountConfigTransferSecureDesktopService
{
    AccountConfigTransferAuthorizationResult AuthorizeStoredCredentialTransfer(
        CredentialStore clonedStore,
        string targetAccountSid);

    AccountConfigTransferAuthorizationResult AuthorizeTypedPasswordTransfer(
        CredentialStore clonedStore,
        string targetAccountSid);
}

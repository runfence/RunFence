using RunFence.Account;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

public class AccountConfigTransferSecureDesktopService(
    IPinService pinService,
    IModalCoordinator modalCoordinator,
    ISidNameCacheService sidNameCache) : IAccountConfigTransferSecureDesktopService
{
    public AccountConfigTransferAuthorizationResult AuthorizeStoredCredentialTransfer(
        CredentialStore clonedStore,
        string targetAccountSid)
    {
        var completed = false;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var pinDialog = new PinDialog(
                PinDialogMode.Verify,
                promptMessage: "Confirm your PIN to authorize account migration.");
            pinDialog.VerifyCallback = pin => pinService.VerifyPin(pin, clonedStore);
            if (pinDialog.ShowDialog() == DialogResult.OK)
                completed = true;
        });

        return new AccountConfigTransferAuthorizationResult(completed, null, clonedStore);
    }

    public AccountConfigTransferAuthorizationResult AuthorizeTypedPasswordTransfer(
        CredentialStore clonedStore,
        string targetAccountSid)
    {
        var completed = false;
        ProtectedString? capturedPassword = null;
        var targetDisplayName = sidNameCache.GetDisplayName(targetAccountSid);
        if (string.IsNullOrWhiteSpace(targetDisplayName))
            targetDisplayName = targetAccountSid;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var pinDialog = new PinDialog(
                PinDialogMode.Verify,
                promptMessage: "Confirm your PIN to authorize account migration.");
            pinDialog.VerifyCallback = pin => pinService.VerifyPin(pin, clonedStore);
            if (pinDialog.ShowDialog() != DialogResult.OK)
                return;

            using var passwordDialog = new PasswordInputDialog(targetDisplayName);
            passwordDialog.TopMost = true;
            if (passwordDialog.ShowDialog() != DialogResult.OK)
                return;

            capturedPassword = passwordDialog.Password;
            completed = true;
        });

        return new AccountConfigTransferAuthorizationResult(completed, capturedPassword, clonedStore);
    }
}

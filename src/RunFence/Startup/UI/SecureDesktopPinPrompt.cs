using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

public class SecureDesktopPinPrompt(
    SessionContext session,
    IPinService pinService,
    ISecureDesktopRunner secureDesktopRunner) : IUnlockPinPrompt
{
    public bool TryVerify()
    {
        var verified = false;
        var store = session.CredentialStore;
        secureDesktopRunner.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify, allowReset: false);
            dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, store);
            if (dlg.ShowDialog() == DialogResult.OK)
                verified = true;
        });
        return verified;
    }
}

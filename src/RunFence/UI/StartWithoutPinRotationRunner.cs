using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.UI;

/// <summary>
/// Production implementation of <see cref="IStartWithoutPinRotationRunner"/>.
/// Opens a Verify-mode <see cref="PinDialog"/> on the secure desktop, verifies the PIN,
/// and re-derives/re-encrypts the credential store to produce a fresh key suitable for
/// <see cref="PinChangeOrchestrator.ApplyKeyRotation"/>.
/// </summary>
public class StartWithoutPinRotationRunner(
    IPinService pinService,
    IModalCoordinator modalCoordinator,
    ILoggingService log)
    : IStartWithoutPinRotationRunner
{
    public PinKeyRotationResult? Run(string promptMessage, SessionContext session)
    {
        PinKeyRotationResult? rotationResult = null;
        DialogResult dlgResult = DialogResult.Cancel;
        var currentKey = session.PinDerivedKey;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify, promptMessage: promptMessage, allowReset: false);
            dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, session.CredentialStore);
            dlg.ProcessingCallback = async (ProtectedString pin, string? _) =>
            {
                try
                {
                    rotationResult = await Task.Run(() => pinService.ChangePin(currentKey, pin, session.CredentialStore));
                    return null;
                }
                catch (Exception ex)
                {
                    rotationResult?.Dispose();
                    rotationResult = null;
                    log.Error("PIN re-encryption failed", ex);
                    return ex.Message;
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult != DialogResult.OK)
        {
            rotationResult?.Dispose();
            return null;
        }

        return rotationResult;
    }
}

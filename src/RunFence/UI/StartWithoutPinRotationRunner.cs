using System.Security.Cryptography;
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
    public PinRotationResult? Run(string promptMessage, SessionContext session)
    {
        byte[]? capturedOldKey = null;
        CredentialStore? capturedStore = null;
        byte[]? capturedKey = null;
        DialogResult dlgResult = DialogResult.Cancel;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify, promptMessage: promptMessage, allowReset: false);
            dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, session.CredentialStore, out capturedOldKey);
            dlg.ProcessingCallback = async (ProtectedString pin, string? _) =>
            {
                try
                {
                    var (resultStore, resultKey) = await Task.Run(() =>
                        pinService.ChangePin(capturedOldKey!, pin, session.CredentialStore));
                    capturedStore = resultStore;
                    capturedKey = resultKey;
                    return null;
                }
                catch (Exception ex)
                {
                    log.Error("PIN re-encryption failed", ex);
                    return ex.Message;
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (capturedOldKey != null)
            CryptographicOperations.ZeroMemory(capturedOldKey);

        if (dlgResult != DialogResult.OK)
            return null;

        return new PinRotationResult(capturedStore!, capturedKey!);
    }
}

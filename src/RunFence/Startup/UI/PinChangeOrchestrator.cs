using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Orchestrates the PIN change flow: opens a PinDialog on a secure desktop,
/// re-encrypts all config, updates the session, and notifies callers after
/// key rotation completes.
/// </summary>
public class PinChangeOrchestrator(
    IPinService pinService,
    IAppConfigService appConfigService,
    IRememberPinService rememberPinService,
    ILoggingService log,
    IModalCoordinator modalCoordinator)
{
    /// <summary>
    /// Runs the full PIN change flow. Calls <paramref name="onPinChanged"/> if the PIN was
    /// successfully changed.
    /// </summary>
    public void Run(
        SessionContext session,
        Action onPinChanged)
    {
        PinKeyRotationResult? rotationResult = null;
        DialogResult dlgResult = DialogResult.Cancel;
        var store = session.CredentialStore;
        var currentKey = session.PinDerivedKey;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (ProtectedString newPin, string? _) =>
            {
                try
                {
                    rotationResult = await Task.Run(() => pinService.ChangePin(currentKey, newPin, store));
                    return null;
                }
                catch (Exception ex)
                {
                    rotationResult?.Dispose();
                    rotationResult = null;
                    log.Error("PIN change failed", ex);
                    return $"PIN change failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult == DialogResult.OK)
        {
            try
            {
                ApplyKeyRotation(session, rotationResult!, onPinChanged, updateRememberPin: true);
                rotationResult = null;
            }
            finally
            {
                rotationResult?.Dispose();
            }

            MessageBox.Show(
                "PIN changed successfully.\r\n\r\n" +
                "Backups of the last loaded configs are still encrypted with the old PIN. " +
                "They will be replaced after those configs are loaded again, such as on the next restart.",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            rotationResult?.Dispose();
        }
    }

    public void ApplyKeyRotation(
        SessionContext session,
        PinKeyRotationResult rotationResult,
        Action onKeyRotated,
        bool updateRememberPin = true)
    {
        ArgumentNullException.ThrowIfNull(rotationResult);

        SecureSecret? ownedNewKey = null;
        try
        {
            ownedNewKey = rotationResult.TakeNewPinDerivedKey();

            // Save to disk before updating in-memory state so a failed save
            // doesn't leave the session pointing at a new key while disk has old data.
            appConfigService.ReencryptAndSaveAll(rotationResult.Store, session.Database, ownedNewKey);

            session.CredentialStore = rotationResult.Store;
            session.ReplacePinDerivedKey(ownedNewKey);
            ownedNewKey = null;
            onKeyRotated();

            if (updateRememberPin)
                TryRefreshRememberPin(session.PinDerivedKey);
        }
        finally
        {
            ownedNewKey?.Dispose();
        }
    }

    private void TryRefreshRememberPin(ISecureSecretSnapshotSource pinDerivedKey)
    {
        try
        {
            rememberPinService.UpdateForPinChange(pinDerivedKey);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to refresh Remember PIN key after PIN rotation; disabling feature: {ex.Message}");
            TryDisableRememberPinAfterFailure();
        }
    }

    private void TryDisableRememberPinAfterFailure()
    {
        try
        {
            rememberPinService.Disable();
        }
        catch (Exception cleanupEx)
        {
            log.Warn($"Failed to clean up Remember PIN key material after PIN rotation error: {cleanupEx.Message}");
        }
    }
}

using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Orchestrates the PIN change flow: opens a PinDialog on a secure desktop,
/// re-encrypts all config, updates the session, and fires the key-change event.
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
    /// successfully changed, passing (oldBuffer, newStore, newPinDerivedKey).
    /// </summary>
    public void Run(
        SessionContext session,
        Action<ProtectedBuffer, CredentialStore, ProtectedBuffer> onPinChanged)
    {
        CredentialStore? newStore = null;
        byte[]? newKey = null;
        DialogResult dlgResult = DialogResult.Cancel;

        using var pinnedKey = PinnedKeyBuffer.FromProtected(session.PinDerivedKey);
        var store = session.CredentialStore;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (ProtectedString newPin, string? _) =>
            {
                try
                {
                    var (resultStore, resultKey) = await Task.Run(() =>
                        pinService.ChangePin(pinnedKey.Data, newPin, store));
                    newStore = resultStore;
                    newKey = resultKey;
                    return null;
                }
                catch (Exception ex)
                {
                    log.Error("PIN change failed", ex);
                    return $"PIN change failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult == DialogResult.OK)
        {
            ProtectedBuffer? newKeyBuffer = null;
            try
            {
                newKeyBuffer = new ProtectedBuffer(newKey!);
                newKey = null;
                ApplyKeyRotation(session, newStore!, newKeyBuffer, onPinChanged, updateRememberPin: true);
                newKeyBuffer = null;
            }
            finally
            {
                newKeyBuffer?.Dispose();
                if (newKey != null)
                    CryptographicOperations.ZeroMemory(newKey);
            }

            MessageBox.Show("PIN changed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (newKey != null)
        {
            CryptographicOperations.ZeroMemory(newKey);
        }
    }

    public void ApplyKeyRotation(
        SessionContext session,
        CredentialStore newStore,
        ProtectedBuffer newKey,
        Action<ProtectedBuffer, CredentialStore, ProtectedBuffer> onKeyRotated,
        bool updateRememberPin = true)
    {
        ProtectedBuffer? ownedNewKey = newKey;
        try
        {
            // Save to disk before updating in-memory state so a failed save
            // doesn't leave the session pointing at a new key while disk has old data.
            using (var scope = ownedNewKey.Unprotect())
            {
                appConfigService.ReencryptAndSaveAll(newStore, session.Database, scope.Data);
            }

            var oldBuffer = session.PinDerivedKey;
            session.CredentialStore = newStore;
            session.PinDerivedKey = ownedNewKey;
            ownedNewKey = null;
            onKeyRotated(oldBuffer, session.CredentialStore, session.PinDerivedKey);

            if (updateRememberPin)
                TryRefreshRememberPin(session.PinDerivedKey);
        }
        finally
        {
            ownedNewKey?.Dispose();
        }
    }

    private void TryRefreshRememberPin(ProtectedBuffer pinDerivedKey)
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

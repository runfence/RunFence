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

        using var pinnedKey = ExtractPinDerivedKey(session.PinDerivedKey);
        var store = session.CredentialStore;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (newPin, _) =>
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
            ApplyPinChange(session, newStore!, newKey!, onPinChanged);
            MessageBox.Show("PIN changed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ApplyPinChange(
        SessionContext session,
        CredentialStore newStore,
        byte[] newKey,
        Action<ProtectedBuffer, CredentialStore, ProtectedBuffer> onPinChanged)
    {
        // Save to disk before updating in-memory state so a failed save
        // doesn't leave the session pointing at a new key while disk has old data.
        appConfigService.ReencryptAndSaveAll(newStore, session.Database, newKey);

        var oldBuffer = session.PinDerivedKey;
        session.CredentialStore = newStore;
        session.PinDerivedKey = new ProtectedBuffer(newKey);
        CryptographicOperations.ZeroMemory(newKey);
        onPinChanged(oldBuffer, session.CredentialStore, session.PinDerivedKey);
    }

    private static PinnedKeyBuffer ExtractPinDerivedKey(ProtectedBuffer pinDerivedKey)
    {
        using var scope = pinDerivedKey.Unprotect();
        return new PinnedKeyBuffer(scope.Data.ToArray());
    }
}
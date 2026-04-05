using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Encapsulates the shared PIN reset flow used by both <see cref="StartupUI"/> and
/// <see cref="LockManager"/>: confirmation prompt → new PIN entry → store creation → save.
/// </summary>
public static class PinResetFlowRunner
{
    /// <summary>
    /// Runs the full PIN reset flow inside the current secure desktop context.
    /// Must be called from within an <see cref="ISecureDesktopRunner.Run"/> callback (or on the UI thread).
    /// </summary>
    /// <param name="pinService">PIN service for deriving and resetting the PIN.</param>
    /// <param name="databaseService">Database service for persisting the new credential store.</param>
    /// <param name="appInit">Initialization helper for populating default IPC callers on reset.</param>
    /// <param name="extraStoreInit">
    /// Optional action invoked on the new <see cref="CredentialStore"/> before saving.
    /// Used by the lock manager flow to call <see cref="IAppInitializationHelper.EnsureCurrentAccountCredential"/>.
    /// </param>
    /// <returns>
    /// The new <see cref="CredentialStore"/> and derived key bytes if the reset completed successfully;
    /// <see langword="null"/> if the user cancelled or skipped.
    /// </returns>
    public static (CredentialStore Store, byte[] Key)? RunResetFlow(
        IPinService pinService,
        IDatabaseService databaseService,
        IAppInitializationHelper appInit,
        Action<CredentialStore>? extraStoreInit = null)
    {
        var confirm = MessageBox.Show(
            "This will delete all stored passwords and app entries.\nContinue?",
            "Reset PIN",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return null;

        CredentialStore? newStore = null;
        byte[]? newKey = null;

        using var setDlg = new PinDialog(PinDialogMode.Set);
        setDlg.ProcessingCallback = async (newPin, _) =>
        {
            try
            {
                (newStore, newKey) = await Task.Run(() => pinService.ResetPin(newPin));

                var resetDb = new AppDatabase();
                appInit.PopulateDefaultIpcCallers(resetDb);
                extraStoreInit?.Invoke(newStore);
                databaseService.SaveCredentialStoreAndConfig(newStore, resetDb, newKey);
                return null;
            }
            catch (Exception ex)
            {
                return $"PIN reset failed: {ex.Message}";
            }
        };

        if (setDlg.ShowDialog() != DialogResult.OK)
            return null;

        return (newStore!, newKey!);
    }
}
using System.Security.Cryptography;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Encapsulates the shared PIN reset flow used by both <see cref="StartupUI"/> and
/// <see cref="LockManager"/>: confirmation prompt → new PIN entry → store creation → save.
/// </summary>
public class PinResetFlowRunner(
    IPinService pinService,
    IDatabaseService databaseService,
    IAppInitializationHelper appInit)
    : IPinResetFlowRunner
{
    /// <inheritdoc/>
    public (CredentialStore Store, byte[] Key)? RunResetFlow(Action<CredentialStore>? extraStoreInit = null)
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
                appInit.InitializeNewDatabase(resetDb);
                extraStoreInit?.Invoke(newStore);
                databaseService.SaveCredentialStoreAndConfig(newStore, resetDb, newKey);
                return null;
            }
            catch (Exception ex)
            {
                if (newKey != null)
                {
                    CryptographicOperations.ZeroMemory(newKey);
                    newKey = null;
                }

                return $"PIN reset failed: {ex.Message}";
            }
        };

        if (setDlg.ShowDialog() != DialogResult.OK)
            return null;

        return (newStore!, newKey!);
    }
}

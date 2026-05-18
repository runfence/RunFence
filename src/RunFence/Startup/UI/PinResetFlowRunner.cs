using System.Diagnostics;
using RunFence.Core;
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
    public PinResetResult? RunResetFlow()
    {
        // Defense-in-depth: this method shows interactive dialogs and must run inside a secure desktop
        // context (ISecureDesktopRunner.Run callback) to prevent UI spoofing attacks.
        // A runtime check is not feasible without a secure desktop detection API.
        Debug.Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA,
            "RunResetFlow must be called on an STA thread inside ISecureDesktopRunner.Run context.");

        var confirm = MessageBox.Show(
            "This will delete all stored passwords and app entries.\nContinue?",
            "Reset PIN",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return null;

        PinResetResult? resetResult = null;

        using var setDlg = new PinDialog(PinDialogMode.Set);
        setDlg.ProcessingCallback = async (ProtectedString newPin, string? _) =>
        {
            try
            {
                resetResult = await Task.Run(() => pinService.ResetPin(newPin));

                var resetDb = new AppDatabase();
                appInit.InitializeNewDatabase(resetDb);
                appInit.EnsureCurrentAccountCredential(resetResult.Store);
                SecureSecret? key = null;
                try
                {
                    key = resetResult.TakePinDerivedKey();
                    databaseService.SaveCredentialStoreAndConfig(resetResult.Store, resetDb, key);
                    resetResult = new PinResetResult(resetResult.Store, key);
                    key = null;
                }
                finally
                {
                    key?.Dispose();
                }

                return null;
            }
            catch (Exception ex)
            {
                resetResult?.Dispose();
                resetResult = null;
                return $"PIN reset failed: {ex.Message}";
            }
        };

        if (setDlg.ShowDialog() != DialogResult.OK)
        {
            resetResult?.Dispose();
            return null;
        }

        return resetResult;
    }
}

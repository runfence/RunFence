using RunFence.Core;
using RunFence.Core.Models;
using RunFence.PrefTrans;

namespace RunFence.Account;

/// <summary>
/// Handles the desktop settings import workflow: per-account import, progress logging,
/// and credential decryption. File selection and progress display are caller responsibilities.
/// </summary>
public class AccountImportHandler(
    ISettingsTransferService settingsTransferService,
    IAccountCredentialManager credentialManager,
    SessionPersistenceHelper persistenceHelper,
    ILoggingService log) : IAccountImportHandler
{
    /// <summary>
    /// Runs the import workflow for the selected accounts.
    /// </summary>
    /// <param name="accounts">Accounts to import to. Accounts without a stored credential entry are skipped.</param>
    /// <param name="credStore">The credential store for password decryption.</param>
    /// <param name="pinKey">The PIN-derived key.</param>
    /// <param name="selectFile">Callback that returns the selected settings file path, or null if cancelled.</param>
    /// <param name="appendLog">Callback to append a line to the progress log.</param>
    /// <param name="enableOk">Callback to enable the OK button when import is complete.</param>
    /// <param name="onStatusUpdate">Callback to update a status label during import.</param>
    /// <returns>The selected settings file path if import completed, or null if the user cancelled.</returns>
    public async Task<string?> RunImportAsync(
        List<ImportAccount> accounts,
        CredentialStore credStore,
        ProtectedBuffer pinKey,
        Func<string?> selectFile,
        Action<string> appendLog,
        Action enableOk,
        Action<string> onStatusUpdate,
        AppDatabase? db = null)
    {
        var settingsPath = selectFile();
        if (settingsPath == null)
            return null;

        if (!File.Exists(settingsPath))
        {
            appendLog("Settings file not found.");
            enableOk();
            return null;
        }

        try
        {
            int completed = 0;
            foreach (var account in accounts)
            {
                completed++;
                onStatusUpdate(accounts.Count == 1
                    ? $"Importing to {account.Username}..."
                    : $"Importing {completed}/{accounts.Count}...");

                appendLog($"Importing to {account.Username}...");

                var lookupStatus = credentialManager.CheckCredential(
                    account.CredEntry.Sid, credStore);

                if (lookupStatus is CredentialLookupStatus.NotFound or CredentialLookupStatus.MissingPassword)
                {
                    appendLog($"  SKIPPED: Cannot decrypt credentials for {account.Username} (status: {lookupStatus}).");
                    continue;
                }

                var result = await Task.Run(() => settingsTransferService.Import(
                    settingsPath, account.CredEntry.Sid));

                if (result.DatabaseModified && db != null)
                    persistenceHelper.SaveConfig(db, pinKey, credStore.ArgonSalt);

                var status = result.Success ? "OK" : "FAILED";
                appendLog($"  [{status}] {result.Message}");
            }

            appendLog("");
            appendLog("Import complete.");
            onStatusUpdate("Import complete.");
        }
        catch (Exception ex)
        {
            appendLog("");
            appendLog($"Error: {ex.Message}");
            onStatusUpdate($"Import error: {ex.Message}");
            log.Error("Import UI error", ex);
        }
        finally
        {
            enableOk();
        }

        return settingsPath;
    }
}

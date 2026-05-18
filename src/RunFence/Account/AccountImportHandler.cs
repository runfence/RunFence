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
    ICredentialDecryptionService credentialDecryption,
    ILoggingService log) : IAccountImportHandler
{
    /// <summary>
    /// Runs the import workflow for the selected accounts.
    /// </summary>
    /// <param name="accounts">Accounts to import to. Accounts without a stored credential entry are skipped.</param>
    /// <param name="credStore">The credential store for password decryption.</param>
    /// <param name="sink">Progress sink for file selection and UI feedback.</param>
    /// <returns>The selected settings file path if import completed, or null if the user cancelled.</returns>
    public async Task<string?> RunImportAsync(
        List<ImportAccount> accounts,
        CredentialStore credStore,
        IImportProgressSink sink)
    {
        var settingsPath = sink.SelectFile();
        if (settingsPath == null)
            return null;

        if (!File.Exists(settingsPath))
        {
            sink.AppendLog("Settings file not found.");
            sink.EnableOk();
            return null;
        }

        try
        {
            int completed = 0;
            foreach (var account in accounts)
            {
                completed++;
                sink.UpdateStatus(accounts.Count == 1
                    ? $"Importing to {account.Username}..."
                    : $"Importing {completed}/{accounts.Count}...");

                sink.AppendLog($"Importing to {account.Username}...");

                var lookupStatus = credentialDecryption.CheckCredential(
                    account.CredEntry.Sid, credStore);

                if (lookupStatus is CredentialLookupStatus.NotFound or CredentialLookupStatus.MissingPassword)
                {
                    sink.AppendLog($"  SKIPPED: Cannot decrypt credentials for {account.Username} (status: {lookupStatus}).");
                    continue;
                }

                var result = await SettingsImportHelper.ImportAsync(
                    settingsPath, account.CredEntry.Sid, settingsTransferService);

                var uiMessage = result.Status switch
                {
                    SettingsImportStatus.Succeeded => "Imported successfully.",
                    SettingsImportStatus.Canceled => "Import canceled due to permission or user decision.",
                    SettingsImportStatus.PartialSuccess => "Import completed with skipped or conflicting items.",
                    _ => result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Import failed."
                };
                var status = result.Status == SettingsImportStatus.Succeeded ? "OK" : "FAILED";
                sink.AppendLog($"  [{status}] {uiMessage}");
            }

            sink.AppendLog("");
            sink.AppendLog("Import complete.");
            sink.UpdateStatus("Import complete.");
        }
        catch (Exception ex)
        {
            sink.AppendLog("");
            sink.AppendLog($"Error: {ex.Message}");
            sink.UpdateStatus($"Import error: {ex.Message}");
            log.Error("Import UI error", ex);
        }
        finally
        {
            sink.EnableOk();
        }

        return settingsPath;
    }
}

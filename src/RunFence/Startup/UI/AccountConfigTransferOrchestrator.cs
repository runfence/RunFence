using RunFence.Account;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Orchestrates the full account config migration flow: PIN verification, account selection,
/// re-encryption of credentials, file copy, and optional deletion of current account data.
/// </summary>
public class AccountConfigTransferOrchestrator(
    IPinService pinService,
    IModalCoordinator modalCoordinator,
    IAccountConfigMigrationService migrationService,
    ILocalGroupMembershipService localGroupMembership,
    ISidNameCacheService sidNameCache,
    ICredentialEncryptionService encryptionService,
    ILoggingService log)
{
    /// <summary>
    /// Returns all enabled administrator accounts other than the current user,
    /// suitable for use as migration targets. Must be called on the UI thread.
    /// </summary>
    public IReadOnlyList<(string displayName, string sid)> GetAvailableAccounts()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var members = localGroupMembership.GetMembersOfGroup("S-1-5-32-544");
        return members
            .Where(m => !localGroupMembership.IsLocalGroup(m.Sid)
                        && localGroupMembership.IsUserAccountEnabled(m.Username)
                        && !string.Equals(m.Sid, currentSid, StringComparison.OrdinalIgnoreCase))
            .Select(m => (displayName: sidNameCache.GetDisplayName(m.Sid), sid: m.Sid))
            .ToList();
    }

    /// <summary>
    /// Runs the full migration flow for the pre-selected target account.
    /// Shows PIN verification and (if needed) password input on the secure desktop.
    /// Must be called on the UI thread.
    /// <paramref name="onExit"/> is invoked after successful migration and user confirmation to exit.
    /// </summary>
    public async Task RunAsync(SessionContext session, string targetSid, string targetDisplayName, Action onExit)
    {
        using var pinnedKey = PinnedKeyBuffer.FromProtected(session.PinDerivedKey);

        var storeCopy = CredentialStoreCloneHelper.CloneStore(session.CredentialStore);

        bool hasStoredCred = storeCopy.Credentials.Any(c =>
            string.Equals(c.Sid, targetSid, StringComparison.OrdinalIgnoreCase)
            && c.EncryptedPassword.Length > 0);

        bool completed = false;
        ProtectedString? capturedPassword = null;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var pinDlg = new PinDialog(PinDialogMode.Verify,
                promptMessage: "Confirm your PIN to authorize account migration.");
            pinDlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, storeCopy, out _);
            if (pinDlg.ShowDialog() != DialogResult.OK)
                return;

            if (!hasStoredCred)
            {
                using var pwDlg = new PasswordInputDialog(targetDisplayName);
                pwDlg.TopMost = true;
                if (pwDlg.ShowDialog() != DialogResult.OK)
                    return;
                capturedPassword = pwDlg.Password;
            }

            completed = true;
        });

        if (!completed)
        {
            capturedPassword?.Dispose();
            return;
        }

        ProtectedString? targetPassword = null;
        try
        {
            if (hasStoredCred)
            {
                var storedCred = storeCopy.Credentials.First(c =>
                    string.Equals(c.Sid, targetSid, StringComparison.OrdinalIgnoreCase));
                targetPassword = encryptionService.Decrypt(storedCred.EncryptedPassword, pinnedKey.Data);
            }
            else
            {
                targetPassword = capturedPassword;
                capturedPassword = null;
            }

            if (migrationService.TargetHasExistingData(targetSid))
            {
                var overwriteResult = MessageBox.Show(
                    $"{targetDisplayName} already has RunFence data. Replace it?",
                    "Overwrite Existing Data?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (overwriteResult != DialogResult.Yes)
                    return;
            }

            var prevCursor = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                await Task.Run(() => migrationService.MigrateToAccount(
                    storeCopy, targetSid, targetPassword!, pinnedKey.Data));
            }
            catch (Exception ex)
            {
                log.Error("Account migration failed", ex);
                MessageBox.Show(
                    $"Migration failed: {ex.Message}",
                    "Migration Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor.Current = prevCursor;
            }

            var deleteResult = MessageBox.Show(
                $"Migration to {targetDisplayName} complete.\n\nDelete current account's RunFence data (config, credentials, license) and exit?",
                "Migration Complete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (deleteResult == DialogResult.Yes)
            {
                try
                {
                    migrationService.DeleteCurrentAccountData();
                }
                catch (Exception ex)
                {
                    log.Error("Failed to delete current account data after migration", ex);
                    MessageBox.Show(
                        $"Migration succeeded but could not delete current data: {ex.Message}\n\nYou may delete the files manually.",
                        "Cleanup Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                onExit();
            }
        }
        finally
        {
            targetPassword?.Dispose();
            capturedPassword?.Dispose();
        }
    }

}

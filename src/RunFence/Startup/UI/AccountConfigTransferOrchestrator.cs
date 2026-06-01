using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;

namespace RunFence.Startup.UI;

/// <summary>
/// Orchestrates the full account config migration flow: PIN verification, account selection,
/// re-encryption of credentials, file copy, and optional deletion of current account data.
/// </summary>
public class AccountConfigTransferOrchestrator(
    IAccountConfigTransferSecureDesktopService secureDesktopService,
    IAccountConfigTransferPromptService promptService,
    IAccountConfigMigrationService migrationService,
    ILocalGroupQueryService localGroupMembership,
    ISidNameCacheService sidNameCache,
    ICredentialEncryptionSpanService encryptionService,
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
    public async Task RunAsync(
        SessionContext session,
        string targetSid,
        Action onExit)
    {
        var storeCopy = CredentialStoreCloneHelper.CloneStore(session.CredentialStore);
        var currentKey = session.PinDerivedKey;

        bool hasStoredCred = storeCopy.Credentials.Any(c =>
            string.Equals(c.Sid, targetSid, StringComparison.OrdinalIgnoreCase)
            && c.EncryptedPassword.Length > 0);

        var authorizationResult = hasStoredCred
            ? secureDesktopService.AuthorizeStoredCredentialTransfer(storeCopy, targetSid)
            : secureDesktopService.AuthorizeTypedPasswordTransfer(storeCopy, targetSid);

        storeCopy = authorizationResult.ReplacementStore;
        ProtectedString? capturedPassword = authorizationResult.CapturedPassword;

        if (!authorizationResult.Completed)
            return;

        ProtectedString? targetPassword = null;
        try
        {
            try
            {
                if (hasStoredCred)
                {
                    var storedCred = storeCopy.Credentials.First(c =>
                        string.Equals(c.Sid, targetSid, StringComparison.OrdinalIgnoreCase));
                    targetPassword = currentKey.TransformSnapshot(
                        key => encryptionService.Decrypt(storedCred.EncryptedPassword, key));
                }
                else
                {
                    targetPassword = capturedPassword;
                    capturedPassword = null;
                }

                if (migrationService.TargetHasExistingData(targetSid) &&
                    !promptService.ConfirmOverwriteExistingData(targetSid))
                    return;

                var prevCursor = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    await Task.Run(() => migrationService.MigrateToAccount(
                        storeCopy, targetSid, targetPassword!, currentKey));
                }
                finally
                {
                    Cursor.Current = prevCursor;
                }
            }
            catch (Exception ex)
            {
                log.Error("Account migration failed", ex);
                promptService.ShowMigrationFailed(targetSid, ex);
                return;
            }

            if (!promptService.ConfirmDeleteCurrentData(targetSid))
                return;

            try
            {
                migrationService.DeleteCurrentAccountData();
            }
            catch (Exception ex)
            {
                log.Error("Failed to delete current account data after migration", ex);
                promptService.ShowCleanupFailed(targetSid, ex);
                onExit();
                return;
            }

            onExit();
        }
        finally
        {
            targetPassword?.Dispose();
            capturedPassword?.Dispose();
        }
    }
}

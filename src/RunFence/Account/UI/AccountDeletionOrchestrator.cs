using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles local account deletion from the accounts grid, including
/// confirmation dialogs, background ACL cleanup after deletion,
/// and deletion of the user profile.
/// </summary>
public class AccountDeletionOrchestrator(
    IAccountLifecycleManager lifecycleManager,
    AccountMigrationOrchestrator migrationHandler,
    ISessionProvider sessionProvider,
    OperationGuard operationGuard,
    ISidResolver sidResolver,
    IProfilePathResolver profilePathResolver,
    AccountDeletionPreflightService deletionPreflightService,
    IAccountDeletionService accountDeletion,
    ITrayBalloonService trayBalloon,
    ILocalUserProvider localUserProvider,
    IAccountMessageBoxService messageBoxService)
{
    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>Raised when the panel status text should be updated.</summary>
    public event Action<string>? StatusUpdateRequested;

    /// <summary>Raised when a long-running operation begins; the panel should disable itself.</summary>
    public event Action? OperationStarted;

    /// <summary>Raised when a long-running operation ends; the panel should re-enable itself.</summary>
    public event Action? OperationEnded;

    public async void DeleteUser(AccountRow accountRow, int selectedIndex)
    {
        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true
            || string.Equals(accountRow.Sid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase);
        if (isCurrentAccount || SidResolutionHelper.IsInteractiveUserSid(accountRow.Sid))
        {
            messageBoxService.Show(
                null,
                isCurrentAccount
                    ? "The current account cannot be deleted."
                    : "The interactive user account cannot be deleted.",
                "Info",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        operationGuard.Begin();
        OperationStarted?.Invoke();
        try
        {
            var session = sessionProvider.GetSession();
            var displayName = accountRow.Credential != null
                ? SidNameResolver.GetDisplayName(accountRow.Credential, sidResolver, session.Database.SidNames, profilePathResolver)
                : accountRow.Username;
            var canContinue = await deletionPreflightService.EnsureNoBlockingProcessesAsync(
                new AccountDeletionPreflightRequest(
                    accountRow.Sid,
                    displayName,
                    accountRow.IsUnavailable,
                    SidResolutionHelper.IsSystemSid(accountRow.Sid)));
            if (!canContinue)
                return;

            var deleteValidation = await lifecycleManager.ValidateDeleteAsync(accountRow.Sid);

            if (deleteValidation.ErrorMessage != null)
            {
                messageBoxService.Show(
                    null,
                    deleteValidation.ErrorMessage,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            var usedBy = session.Database.Apps.Where(a =>
                string.Equals(a.AccountSid, accountRow.Sid, StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();

            var confirmMessage = $"Delete Windows account '{displayName}'?\n\n" +
                                 "This will:\n" +
                                 "\u2022 Delete the Windows account\n" +
                                 "\u2022 Remove stored credentials (if any)\n" +
                                 "\u2022 Delete the profile folder\n\n";
            if (usedBy.Count > 0)
                confirmMessage += $"This credential is used by: {string.Join(", ", usedBy)}\nThose apps will fail to launch.\n\n";
            confirmMessage += "This cannot be undone.";

            var confirm = messageBoxService.Show(
                null,
                confirmMessage,
                "Confirm Delete Account",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                var deleteResult = await accountDeletion.DeleteAccountAsync(accountRow.Sid, accountRow.Username,
                    session.CredentialStore, removeApps: false);
                foreach (var warning in deleteResult.Warnings)
                    trayBalloon.ShowWarning(warning);
            }
            catch (InvalidOperationException ex)
            {
                messageBoxService.Show(
                    null,
                    $"Failed to delete account: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            localUserProvider.InvalidateCache();
            SaveAndRefreshRequested?.Invoke(null, selectedIndex);

            // App entries referencing the deleted account's SID are intentionally left in place — the user created them manually and can decide later what to do with them (edit to use a different account, or delete).
            StartBackgroundAclCleanup(accountRow.Sid);
        }
        finally
        {
            operationGuard.End();
            OperationEnded?.Invoke();
        }
    }

    private void StartBackgroundAclCleanup(string sid)
        => _ = migrationHandler.StartBackgroundAclCleanupAsync(sid,
            text => StatusUpdateRequested?.Invoke(text),
            () => true);
}

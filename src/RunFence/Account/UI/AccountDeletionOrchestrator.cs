using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
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
    IAccountDeletionService accountDeletion,
    ILocalUserProvider localUserProvider)
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
            MessageBox.Show(isCurrentAccount
                    ? "The current account cannot be deleted."
                    : "The interactive user account cannot be deleted.",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        operationGuard.Begin();
        OperationStarted?.Invoke();
        try
        {
            var validationError = await lifecycleManager.ValidateDeleteAsync(accountRow.Sid);
            if (validationError != null)
            {
                MessageBox.Show(validationError, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var session = sessionProvider.GetSession();
            var displayName = accountRow.Credential != null
                ? SidNameResolver.GetDisplayName(accountRow.Credential, sidResolver, session.Database.SidNames)
                : accountRow.Username;

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

            var confirm = MessageBox.Show(confirmMessage,
                "Confirm Delete Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                accountDeletion.DeleteAccount(accountRow.Sid, accountRow.Username,
                    session.CredentialStore, removeApps: false);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"Failed to delete account: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var profileError = await lifecycleManager.DeleteProfileAsync(accountRow.Sid);
            if (profileError != null)
            {
                MessageBox.Show($"Account deleted, but failed to delete profile folder:\n{profileError}",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

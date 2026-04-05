using System.Security;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles local account creation and deletion from the accounts grid, including
/// the create-user dialog flow, ephemeral account registration, and background ACL cleanup
/// after account deletion.
/// </summary>
public class AccountCreationOrchestrator(
    IAccountCredentialManager credentialManager,
    IAccountLifecycleManager lifecycleManager,
    ILocalUserProvider localUserProvider,
    IAccountRestrictionService accountRestriction,
    AccountMigrationOrchestrator migrationHandler,
    ISessionProvider sessionProvider,
    OperationGuard operationGuard,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache,
    Func<EditAccountDialog> editAccountDialogFactory,
    IAccountDeletionService accountDeletion,
    ILicenseService licenseService,
    AccountPostCreateSetupService postCreateSetup)
{
    /// <summary>Raised when packages need to be installed after account creation.</summary>
    public event Action<List<InstallablePackage>, CredentialEntry, SecureString?>? InstallPackagesRequested;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>Raised when the panel status text should be updated.</summary>
    public event Action<string>? StatusUpdateRequested;

    /// <summary>Raised when a long-running operation begins; the panel should disable itself.</summary>
    public event Action? OperationStarted;

    /// <summary>Raised when a long-running operation ends; the panel should re-enable itself.</summary>
    public event Action? OperationEnded;

    public async void OpenCreateUserDialog(string? prefillUsername = null, string? prefillPassword = null)
    {
        var session = sessionProvider.GetSession();
        if (!EvaluationLimitHelper.CheckCredentialLimit(licenseService, session.CredentialStore.Credentials,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        var hiddenCount = session.CredentialStore.Credentials.Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
        using var dlg = editAccountDialogFactory();
        dlg.InitializeForCreate(prefillUsername, prefillPassword, hiddenCount);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        var password = dlg.CreatedPassword;
        try
        {
            // Re-read session after dialog closes (it may have been modified)
            session = sessionProvider.GetSession();
            var credId = credentialManager.StoreCreatedUserCredential(
                dlg.CreatedSid!, password!,
                session.CredentialStore, session.PinDerivedKey);

            if (credId == null)
                return; // duplicate SID

            localUserProvider.InvalidateCache();
            sidNameCache.ResolveAndCache(dlg.CreatedSid!, dlg.NewUsername!);

            var createdEntry = session.Database.GetOrCreateAccount(dlg.CreatedSid!);
            if (dlg.IsEphemeral)
                createdEntry.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

            // Sync split token default
            createdEntry.SplitTokenOptOut = !dlg.UseSplitTokenDefault;

            // Sync low integrity default
            if (dlg.UseLowIntegrityDefault)
                createdEntry.LowIntegrityDefault = true;

            // Apply firewall settings to DB if changed from defaults
            if (dlg.FirewallSettingsChanged)
            {
                var fwSettings = new FirewallAccountSettings
                {
                    AllowInternet = dlg.AllowInternet,
                    AllowLocalhost = dlg.AllowLocalhost,
                    AllowLan = dlg.AllowLan
                };
                FirewallAccountSettings.UpdateOrRemove(session.Database, dlg.CreatedSid!, fwSettings);
            }

            // Set the first-account warning flag before saving so it is persisted in the same write as the new account.
            bool showFirstAccountWarning = !session.Database.Settings.HasShownFirstAccountWarning;
            if (showFirstAccountWarning)
                session.Database.Settings.HasShownFirstAccountWarning = true;

            bool hasPackages = dlg.SelectedInstallPackages.Count > 0;
            bool internetBlocked = dlg is { FirewallSettingsChanged: true, AllowInternet: false };

            var setupRequest = new PostCreateSetupRequest(
                SettingsImportPath: dlg.SettingsImportPath,
                CreatedSid: dlg.CreatedSid!,
                NewUsername: dlg.NewUsername!,
                Password: password,
                FirewallSettingsChanged: dlg.FirewallSettingsChanged,
                SelectedInstallPackages: hasPackages && internetBlocked
                    ? dlg.SelectedInstallPackages.ToList()
                    : [],
                AllowInternet: dlg.AllowInternet,
                Errors: dlg.Errors);

            await postCreateSetup.RunPostCreateSetupAsync(
                setupRequest,
                () => SaveAndRefreshRequested?.Invoke(credId, -1));

            if (hasPackages && !internetBlocked && InstallPackagesRequested != null)
            {
                var refreshedSession = sessionProvider.GetSession();
                var credEntry = refreshedSession.CredentialStore.Credentials.FirstOrDefault(c => c.Id == credId);
                if (credEntry != null)
                    InstallPackagesRequested.Invoke(dlg.SelectedInstallPackages.ToList(), credEntry, dlg.CreatedPassword);
            }

            if (dlg.Errors.Count > 0)
            {
                var msg = "Account created and credential stored, but some options failed:\n\n"
                          + string.Join("\n", dlg.Errors.Select(e => "\u2022 " + e));
                MessageBox.Show(msg, "Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (showFirstAccountWarning)
            {
                MessageBox.Show(
                    "Some features (e.g. opening URLs) may not work until the account has been logged into at least once.\n\n" +
                    "To do a first-time login:\n" +
                    "1. Turn on \"Logon\" for this account\n" +
                    "2. Click \"Set empty password\"\n" +
                    "3. Lock Windows (Win+L) and log in as the new account, then sign out\n" +
                    "4. Come back here, turn \"Logon\" back off and click \"Rotate account password\"",
                    "First-Time Login Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        finally
        {
            password?.Dispose();
        }
    }

    public async void DeleteUser(AccountRow accountRow, int selectedIndex)
    {
        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true;
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
using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

/// <summary>
/// Handles credential CRUD operations for the accounts grid: add, edit, remove credential,
/// and the full edit-account flow (password change, desktop settings import, ephemeral toggle).
/// </summary>
public class AccountCredentialOperations(
    IAccountCredentialManager credentialManager,
    ILocalUserProvider localUserProvider,
    IAccountRestrictionService accountRestriction,
    AccountEditHelper editHelper,
    ISessionProvider sessionProvider,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache,
    Func<EditAccountDialog> editAccountDialogFactory,
    Func<CredentialEditDialog> credentialEditDialogFactory,
    ILicenseService licenseService,
    AccountLauncher launcher)
{
    private Control _ownerControl = null!;

    public event Action<IReadOnlyList<InstallablePackage>, AccountRow>? InstallPackagesRequested;

    /// <summary>Raised when the credential operations need to open the create user dialog.</summary>
    public event Action<string?, string?>? CreateUserDialogRequested;

    /// <summary>Raised when a delete user action is triggered from the credential edit dialog.</summary>
    public event Action<AccountRow, int>? DeleteUserRequested;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>
    /// Binds the handler to the owner control. Must be called before any operations.
    /// <paramref name="ownerControl"/> is disabled during long-running operations such as desktop settings import.
    /// </summary>
    public void Initialize(Control ownerControl)
    {
        _ownerControl = ownerControl;
    }

    /// <summary>
    /// Opens the add credential dialog. <paramref name="selectedRow"/> is the currently selected row,
    /// used to pre-populate the username field. Pass null if nothing is selected.
    /// </summary>
    public void AddCredential(AccountRow? selectedRow)
    {
        var session = sessionProvider.GetSession();
        if (!EvaluationLimitHelper.CheckCredentialLimit(licenseService, session.CredentialStore.Credentials,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        // Interactive user without stored credentials is already in the Credentials section;
        // redirect to Edit flow which opens an add-mode dialog without the existingSids block.
        if (selectedRow is { Credential: null }
            && SidResolutionHelper.IsInteractiveUserSid(selectedRow.Sid))
        {
            EditCredential(selectedRow);
            return;
        }

        var existingSids = session.CredentialStore.Credentials.Select(c => c.Sid).ToList();
        // Also include the interactive user SID to prevent adding a duplicate credential
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null && !existingSids.Contains(interactiveSid, StringComparer.OrdinalIgnoreCase))
            existingSids.Add(interactiveSid);

        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        var allLocalUsers = localUserProvider.GetLocalUserAccounts();
        // Filter dropdown to only show accounts that can actually have a credential added:
        // exclude accounts already with credentials, the interactive user, and the current account.
        var availableLocalUsers = allLocalUsers
            .Where(u => !existingSids.Any(s => string.Equals(s, u.Sid, StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(u.Sid, currentSid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Pre-populate username from the currently selected account row, if applicable.
        string? defaultUsername = null;
        if (selectedRow is { IsUnavailable: false, Credential: null }
            && !string.Equals(selectedRow.Sid, currentSid, StringComparison.OrdinalIgnoreCase)
            && !existingSids.Any(s => string.Equals(s, selectedRow.Sid, StringComparison.OrdinalIgnoreCase)))
        {
            defaultUsername = selectedRow.Username;
        }

        DialogResult result = DialogResult.None;
        string? sid = null;
        string username = "";
        SecureString? password = null;
        bool openCreateUser = false;
        string? capturedPasswordText = null;

        DataPanel.RunOnSecureDesktop(() =>
        {
            using var dlg = credentialEditDialogFactory();
            dlg.Initialize(localUsers: availableLocalUsers, defaultUsername: defaultUsername,
                sidNames: sessionProvider.GetSession().Database.SidNames, existingSids: existingSids);
            result = dlg.ShowDialog();
            sid = dlg.Sid;
            username = dlg.Username;
            password = dlg.Password;
            openCreateUser = dlg.OpenCreateUser;
            capturedPasswordText = dlg.CapturedPasswordText;
        });

        if (result == DialogResult.Retry && openCreateUser)
        {
            CreateUserDialogRequested?.Invoke(username, capturedPasswordText);
            return;
        }

        if (result != DialogResult.OK)
            return;

        try
        {
            AddNewCredential(sid!, username, password);
        }
        finally
        {
            password?.Dispose();
        }
    }

    /// <summary>
    /// Opens the edit credential dialog for <paramref name="accountRow"/>.
    /// Caller must ensure the row is selected and not unavailable.
    /// </summary>
    public void EditCredential(AccountRow accountRow)
    {
        if (accountRow.IsUnavailable)
            return;

        if (accountRow.Credential?.IsCurrentAccount == true)
        {
            MessageBox.Show("The current account entry cannot be edited.",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (accountRow.Credential != null)
        {
            var credEntry = accountRow.Credential;
            var hasStoredPassword = credEntry.EncryptedPassword.Length > 0;
            var localUsers = localUserProvider.GetLocalUserAccounts();

            DialogResult result = DialogResult.None;
            SecureString? password = null;

            DataPanel.RunOnSecureDesktop(() =>
            {
                using var dlg = credentialEditDialogFactory();
                dlg.Initialize(credEntry, hasStoredPassword, localUsers,
                    sidNames: sessionProvider.GetSession().Database.SidNames);
                result = dlg.ShowDialog();
                password = dlg.Password;
            });

            if (result != DialogResult.OK)
                return;

            try
            {
                var session = sessionProvider.GetSession();
                if (password != null)
                    credentialManager.UpdateCredentialPassword(credEntry, password, session.PinDerivedKey);

                SaveAndRefreshRequested?.Invoke(credEntry.Id, -1);
            }
            finally
            {
                password?.Dispose();
            }
        }
        else
        {
            var session = sessionProvider.GetSession();
            var localUsers = localUserProvider.GetLocalUserAccounts();
            var existingSids = session.CredentialStore.Credentials.Select(c => c.Sid).ToList();

            DialogResult result = DialogResult.None;
            string? sid = null;
            string username = "";
            SecureString? password = null;

            DataPanel.RunOnSecureDesktop(() =>
            {
                using var dlg = credentialEditDialogFactory();
                dlg.Initialize(localUsers: localUsers, defaultUsername: accountRow.Username,
                    sidNames: session.Database.SidNames, existingSids: existingSids);
                result = dlg.ShowDialog();
                sid = dlg.Sid;
                username = dlg.Username;
                password = dlg.Password;
            });

            if (result != DialogResult.OK)
                return;

            try
            {
                AddNewCredential(sid!, username, password);
            }
            finally
            {
                password?.Dispose();
            }
        }
    }

    private void AddNewCredential(string sid, string username, SecureString? password)
    {
        var session = sessionProvider.GetSession();
        if (!EvaluationLimitHelper.CheckCredentialLimit(licenseService, session.CredentialStore.Credentials,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        var (success, credId, error) = credentialManager.AddNewCredential(
            sid, password,
            session.CredentialStore, session.PinDerivedKey);

        if (!success)
        {
            MessageBox.Show(error!, "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        sidNameCache.ResolveAndCache(sid, username);

        SaveAndRefreshRequested?.Invoke(credId, -1);
    }

    /// <summary>
    /// Removes the credential for <paramref name="accountRow"/>.
    /// <paramref name="selectedIndex"/> is used to restore grid selection after removal.
    /// Caller must ensure the row is not unavailable.
    /// </summary>
    public void RemoveCredential(AccountRow accountRow, int selectedIndex)
    {
        if (accountRow.IsUnavailable)
            return;

        if (accountRow.Credential == null)
            return;

        var credEntry = accountRow.Credential;
        if (credEntry.IsCurrentAccount)
        {
            MessageBox.Show("The current account entry cannot be removed.",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var session = sessionProvider.GetSession();
        var displayName = SidNameResolver.GetDisplayName(credEntry, sidResolver, session.Database.SidNames);
        var usedBy = session.Database.Apps.Where(a =>
            string.Equals(a.AccountSid, credEntry.Sid, StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();

        var confirmMessage = usedBy.Count > 0
            ? $"Remove credential '{displayName}'?\n\nThis credential is used by: {string.Join(", ", usedBy)}\nThose apps will fail to launch until a new credential is added."
            : $"Remove credential '{displayName}'?";

        if (MessageBox.Show(confirmMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            credentialManager.RemoveCredential(credEntry.Id, session.CredentialStore);
            SaveAndRefreshRequested?.Invoke(null, selectedIndex);
        }
    }

    /// <summary>
    /// Opens the Edit Account dialog for <paramref name="accountRow"/>.
    /// <paramref name="selectedIndex"/> is used to restore grid selection if no credential is associated.
    /// Caller must ensure the row is not unavailable.
    /// </summary>
    public async void EditAccount(AccountRow accountRow, int selectedIndex)
    {
        if (accountRow.IsUnavailable)
            return;

        var session = sessionProvider.GetSession();
        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true;
        var acctEntry = session.Database.GetAccount(accountRow.Sid);
        var isSplitTokenDefault = acctEntry?.SplitTokenOptOut != true;
        var isLowIntegrityDefault = acctEntry?.LowIntegrityDefault == true;

        var hiddenCount = session.CredentialStore.Credentials
            .Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
        var currentFirewallSettings = acctEntry?.Firewall;
        var canInstall = SidResolutionHelper.CanLaunchWithoutPassword(accountRow.Sid) || accountRow.HasStoredPassword;
        var dlg = editAccountDialogFactory();
        dlg.InitializeForEdit(
            accountRow.Sid, accountRow.Username, accountRow.IsEphemeral,
            isCurrentAccount: isCurrentAccount,
            isSplitTokenDefault: isSplitTokenDefault,
            isLowIntegrityDefault: isLowIntegrityDefault,
            currentHiddenCount: hiddenCount,
            firewallSettings: currentFirewallSettings,
            launcher: launcher,
            canInstall: canInstall);
        using (dlg)
        {
            var dialogResult = DataPanel.ShowModal(dlg, _ownerControl.FindForm());
            if (dlg.DeleteRequested)
            {
                DeleteUserRequested?.Invoke(accountRow, selectedIndex);
                return;
            }

            if (dialogResult != DialogResult.OK)
            {
                SaveAndRefreshRequested?.Invoke(accountRow.Credential?.Id, -1);
                return;
            }

            // Re-read session for database mutations
            session = sessionProvider.GetSession();
            if (dlg.NewUsername != null)
                sidNameCache.UpdateName(accountRow.Sid, $"{Environment.MachineName}\\{dlg.NewUsername}");

            // Password change (after dialog confirmation so the new text is available)
            var passwordApplied = editHelper.ApplyPasswordChange(accountRow, dlg, isCurrentAccount);
            if (dlg.NewPasswordText != null)
            {
                if (passwordApplied && accountRow.Credential != null && dlg.NewPassword != null)
                    credentialManager.UpdateCredentialPassword(accountRow.Credential, dlg.NewPassword, session.PinDerivedKey);
                dlg.NewPassword?.Dispose();
            }

            // Desktop settings import (after password change so the new password is valid)
            var effectiveUsername = dlg.NewUsername ?? accountRow.Username;
            await editHelper.ImportDesktopSettingsAsync(accountRow, dlg, effectiveUsername, passwordApplied, isCurrentAccount, _ownerControl);

            // Update ephemeral state (only when changed)
            if (dlg.IsEphemeral != accountRow.IsEphemeral)
            {
                var entry = session.Database.GetOrCreateAccount(accountRow.Sid);
                entry.DeleteAfterUtc = dlg.IsEphemeral ? DateTime.UtcNow.AddHours(24) : null;
                if (!dlg.IsEphemeral)
                    session.Database.RemoveAccountIfEmpty(accountRow.Sid);
            }

            // Sync split token default (admin accounts only)
            if (dlg.IsSplitTokenApplicable)
                session.Database.GetOrCreateAccount(accountRow.Sid).SplitTokenOptOut = !dlg.UseSplitTokenDefault;

            // Sync low integrity default
            session.Database.GetOrCreateAccount(accountRow.Sid).LowIntegrityDefault = dlg.UseLowIntegrityDefault;
            session.Database.RemoveAccountIfEmpty(accountRow.Sid);

            localUserProvider.InvalidateCache();

            // Update firewall settings in database if changed
            FirewallAccountSettings? newFirewallSettings = null;
            if (dlg.FirewallSettingsChanged)
            {
                newFirewallSettings = new FirewallAccountSettings
                {
                    AllowInternet = dlg.AllowInternet,
                    AllowLocalhost = dlg.AllowLocalhost,
                    AllowLan = dlg.AllowLan,
                    Allowlist = currentFirewallSettings?.Allowlist ?? new List<FirewallAllowlistEntry>()
                };
                FirewallAccountSettings.UpdateOrRemove(session.Database, accountRow.Sid, newFirewallSettings);
            }

            if (dlg.Errors.Count > 0)
            {
                var bullets = string.Join("\n", dlg.Errors.Select(err => $"\u2022 {err}"));
                MessageBox.Show($"Some changes could not be applied:\n{bullets}",
                    "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (accountRow.Credential != null)
                SaveAndRefreshRequested?.Invoke(accountRow.Credential.Id, -1);
            else
                SaveAndRefreshRequested?.Invoke(null, selectedIndex);

            // Apply firewall OS rules AFTER DB is persisted to disk, so OS state never diverges
            // from persisted state. ApplyFirewallRules catches and logs errors internally.
            editHelper.ApplyFirewallRules(accountRow, newFirewallSettings);

            if (dlg.SelectedInstallPackages.Count > 0)
                InstallPackagesRequested?.Invoke(dlg.SelectedInstallPackages, accountRow);
        }
    }
}
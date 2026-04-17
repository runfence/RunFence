using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles credential CRUD operations for the accounts grid: add, edit, remove credential,
/// and the full edit-account flow (password change, desktop settings import, ephemeral toggle).
/// </summary>
/// <remarks>Deps above threshold: Add/Edit/Remove credential + EditAccount share <c>_credentialManager</c>, <c>_sessionProvider</c>, <c>_localUserProvider</c>, <c>_sidNameCache</c>, <c>_licenseService</c> (5 deps). Any split (e.g., credential CRUD vs account edit) duplicates these 5, creating two 8-dep classes instead of one 11-dep class — no improvement. Reviewed 2026-04-08.</remarks>
public class AccountCredentialOperations(
    IModalCoordinator modalCoordinator,
    IAccountCredentialManager credentialManager,
    ILocalUserProvider localUserProvider,
    IAccountLoginRestrictionService accountRestriction,
    AccountEditHelper editHelper,
    ISessionProvider sessionProvider,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache,
    Func<EditAccountDialog> editAccountDialogFactory,
    Func<CredentialEditDialog> credentialEditDialogFactory,
    IEvaluationLimitHelper evaluationLimitHelper,
    PackageInstallService packageInstallService)
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
        if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
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

        modalCoordinator.RunOnSecureDesktop(() =>
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
            capturedPasswordText = null;
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
            var localUsers = localUserProvider.GetLocalUserAccounts();

            DialogResult result = DialogResult.None;
            SecureString? password = null;

            modalCoordinator.RunOnSecureDesktop(() =>
            {
                using var dlg = credentialEditDialogFactory();
                dlg.Initialize(credEntry, localUsers,
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

            modalCoordinator.RunOnSecureDesktop(() =>
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
        if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
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
        var privilegeLevel = acctEntry?.PrivilegeLevel ?? PrivilegeLevel.Basic;

        var hiddenCount = session.CredentialStore.Credentials
            .Count(c => accountRestriction.IsLoginBlockedBySid(c.Sid));
        var currentFirewallSettings = acctEntry?.Firewall;
        var canInstall = SidResolutionHelper.CanLaunchWithoutPassword(accountRow.Sid) || accountRow.HasStoredPassword;
        var dlg = editAccountDialogFactory();
        dlg.InitializeForEdit(
            accountRow.Sid, accountRow.Username, accountRow.IsEphemeral,
            isCurrentAccount: isCurrentAccount,
            privilegeLevel: privilegeLevel,
            currentHiddenCount: hiddenCount,
            firewallSettings: currentFirewallSettings,
            packageInstallService: packageInstallService,
            canInstall: canInstall);
        using (dlg)
        {
            var dialogResult = modalCoordinator.ShowModal(dlg, _ownerControl.FindForm());
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
            Guid? newCredentialId = null;
            var passwordApplied = editHelper.ApplyPasswordChange(accountRow, dlg, isCurrentAccount);
            if (dlg.NewPasswordText != null)
            {
                if (passwordApplied && dlg.NewPassword != null)
                {
                    if (accountRow.Credential is { IsCurrentAccount: false })
                        credentialManager.UpdateCredentialPassword(accountRow.Credential, dlg.NewPassword, session.PinDerivedKey);
                    else if (accountRow.Credential == null
                             && evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
                                 extraMessage: "Right-click any credential in the list to remove it."))
                    {
                        var (_, credId, _) = credentialManager.AddNewCredential(
                            accountRow.Sid, dlg.NewPassword, session.CredentialStore, session.PinDerivedKey);
                        newCredentialId = credId;
                    }
                }
                dlg.NewPassword?.Dispose();
            }

            // Desktop settings import (after password change so the new password is valid)
            await editHelper.ImportDesktopSettingsAsync(accountRow, dlg, _ownerControl);

            // Update ephemeral state (only when changed)
            if (dlg.IsEphemeral != accountRow.IsEphemeral)
            {
                var entry = session.Database.GetOrCreateAccount(accountRow.Sid);
                entry.DeleteAfterUtc = dlg.IsEphemeral ? DateTime.UtcNow.AddHours(24) : null;
                if (!dlg.IsEphemeral)
                    session.Database.RemoveAccountIfEmpty(accountRow.Sid);
            }

            session.Database.GetOrCreateAccount(accountRow.Sid).PrivilegeLevel = dlg.SelectedPrivilegeLevel;
            session.Database.RemoveAccountIfEmpty(accountRow.Sid);

            localUserProvider.InvalidateCache();

            // Update firewall settings in database if changed
            FirewallAccountSettings? newFirewallSettings = null;
            FirewallAccountSettings? previousFirewallSettings = null;
            if (dlg.FirewallSettingsChanged)
            {
                previousFirewallSettings = (session.Database.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings()).Clone();
                newFirewallSettings = new FirewallAccountSettings
                {
                    AllowInternet = dlg.AllowInternet,
                    AllowLocalhost = dlg.AllowLocalhost,
                    AllowLan = dlg.AllowLan,
                    Allowlist = currentFirewallSettings?.Allowlist ?? new List<FirewallAllowlistEntry>(),
                    LocalhostPortExemptions = currentFirewallSettings?.LocalhostPortExemptions.ToList() ?? ["53", "49152-65535"]
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
            else if (newCredentialId != null)
                SaveAndRefreshRequested?.Invoke(newCredentialId, -1);
            else
                SaveAndRefreshRequested?.Invoke(null, selectedIndex);

            // Apply firewall OS rules AFTER DB is persisted to disk, so OS state never diverges
            // from persisted state. ApplyFirewallRules catches and logs errors internally.
            editHelper.ApplyFirewallRules(accountRow, previousFirewallSettings, newFirewallSettings);

            if (dlg.SelectedInstallPackages.Count > 0)
                InstallPackagesRequested?.Invoke(dlg.SelectedInstallPackages, accountRow);
        }
    }
}

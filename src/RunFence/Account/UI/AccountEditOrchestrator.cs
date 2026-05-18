using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the full edit-account flow: opens <see cref="EditAccountDialog"/>, applies password
/// changes, desktop settings import, ephemeral toggle, privilege level, and firewall settings.
/// </summary>
public class AccountEditOrchestrator(
    ISessionProvider sessionProvider,
    IAccountLoginRestrictionService accountRestriction,
    AccountEditHelper editHelper,
    Func<EditAccountDialog> editAccountDialogFactory,
    IModalCoordinator modalCoordinator,
    ISidNameCacheService sidNameCache,
    IEvaluationLimitHelper evaluationLimitHelper,
    IAccountCredentialManager credentialManager,
    ILocalUserProvider localUserProvider,
    IPackageInstallService packageInstallService)
{
    private Control _ownerControl = null!;

    /// <summary>Raised when the panel should save the session and refresh the grid.</summary>
    public event Action<Guid?, int>? SaveAndRefreshRequested;

    /// <summary>Raised when a delete user action is triggered from the credential edit dialog.</summary>
    public event Action<AccountRow, int>? DeleteUserRequested;

    /// <summary>Raised when install packages are selected during account edit.</summary>
    public event Action<IReadOnlyList<InstallablePackage>, AccountRow>? InstallPackagesRequested;

    /// <summary>
    /// Binds the handler to the owner control. Must be called before any operations.
    /// <paramref name="ownerControl"/> is disabled during long-running operations such as desktop settings import.
    /// </summary>
    public void Initialize(Control ownerControl)
    {
        _ownerControl = ownerControl;
    }

    /// <summary>
    /// Opens the Edit Account dialog for <paramref name="accountRow"/>.
    /// <paramref name="selectedIndex"/> is used to restore grid selection if no credential is associated.
    /// Caller must ensure the row is not unavailable.
    /// </summary>
    public async Task EditAccount(AccountRow accountRow, int selectedIndex)
    {
        if (accountRow.IsUnavailable)
            return;

        var session = sessionProvider.GetSession();
        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true;
        var acctEntry = session.Database.GetAccount(accountRow.Sid);
        var privilegeLevel = acctEntry?.PrivilegeLevel ?? PrivilegeLevel.Isolated;

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
            if (dlg.NewPassword != null)
            {
                var pinKeySource = session.PinDerivedKey;
                if (passwordApplied)
                {
                    if (accountRow.Credential is { IsCurrentAccount: false })
                        credentialManager.UpdateCredentialPassword(accountRow.Credential, dlg.NewPassword, pinKeySource);
                    else if (accountRow.Credential == null
                             && evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials,
                                 extraMessage: "Right-click any credential in the list to remove it."))
                    {
                        var (_, credId, _) = credentialManager.AddNewCredential(
                            accountRow.Sid, dlg.NewPassword, session.CredentialStore, pinKeySource);
                        newCredentialId = credId;
                    }
                }
                dlg.NewPassword.Dispose();
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
                    LocalhostPortExemptions = currentFirewallSettings?.LocalhostPortExemptions.ToList() ?? ["53", "49152-65535"],
                    FilterEphemeralLoopback = currentFirewallSettings?.FilterEphemeralLoopback ?? true
                };
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

            // Apply firewall changes after the rest of the account edit has been saved.
            // The firewall helper handles the persist-before-tighten / loosen-before-persist ordering.
            editHelper.ApplyFirewallRules(accountRow, previousFirewallSettings, newFirewallSettings);

            if (dlg.SelectedInstallPackages.Count > 0)
                InstallPackagesRequested?.Invoke(dlg.SelectedInstallPackages, accountRow);
        }
    }
}

using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.UI;

namespace RunFence.Account.UI;

public class EditAccountDialogCreateHandler(
    IWindowsAccountService windowsAccountService,
    ILocalGroupMutationService groupMembership,
    IAccountRestrictionCoordinator restrictionCoordinator,
    ILicenseService licenseService,
    IUiThreadInvoker uiThreadInvoker,
    IAppStateProvider appState,
    SessionContext session,
    IDatabaseService databaseService,
    ISidNameCacheService sidNameCache)
{
    public record CreateAccountRequest(
        string Username,
        ProtectedString Password,
        ProtectedString ConfirmPassword,
        bool IsEphemeral,
        List<(string Sid, string Name)> CheckedGroups,
        List<(string Sid, string Name)> UncheckedGroups,
        bool AllowLogon,
        bool AllowNetworkLogin,
        bool AllowBgAutorun,
        int CurrentHiddenCount)
    {
        /// <summary>
        /// Creates a standard isolated account request: removed from Users group, no logon/network/bg,
        /// hidden count zero. Used by wizard templates that create sandboxed isolated accounts.
        /// The caller's <paramref name="password"/> can be safely disposed after this method returns;
        /// independent copies are stored in the returned request.
        /// </summary>
        public static CreateAccountRequest ForIsolatedAccount(string username, ProtectedString password, bool isEphemeral = false)
            => new(
                Username: username,
                Password: password.Copy(),
                ConfirmPassword: password.Copy(),
                IsEphemeral: isEphemeral,
                CheckedGroups: [],
                UncheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
                AllowLogon: false,
                AllowNetworkLogin: false,
                AllowBgAutorun: false,
                CurrentHiddenCount: 0);
    }

    /// <summary>
    /// Set after Execute returns a non-success status that should be shown inline in the status label.
    /// </summary>
    public string? LastValidationError { get; private set; }

    /// <summary>
     /// Creates a local Windows user account with the specified settings.
    /// Validation and Windows account creation failures return a non-success status with
    /// <see cref="LastValidationError"/> populated for inline display. When the Windows account
    /// is created but saving RunFence cleanup state fails, the result keeps the created SID/name
    /// while omitting the password so callers can stop further setup without losing in-memory cleanup.
    /// </summary>
    public CreateAccountResult Execute(CreateAccountRequest request)
    {
        LastValidationError = null;

        // Validate username
        if (request.Username.Length is 0 or > 20)
        {
            LastValidationError = "Account name must be 1\u201320 characters.";
            return new CreateAccountResult(
                CreateAccountStatus.ValidationFailed,
                string.Empty,
                null,
                request.Username,
                request.IsEphemeral,
                [],
                LastValidationError);
        }

        if (request.Username.IndexOfAny(EditAccountDialog.InvalidNameChars) >= 0)
        {
            LastValidationError = "Account name contains invalid characters.";
            return new CreateAccountResult(
                CreateAccountStatus.ValidationFailed,
                string.Empty,
                null,
                request.Username,
                request.IsEphemeral,
                [],
                LastValidationError);
        }

        if (request.Password.Length > 0 && !ProtectedString.ContentEqual(request.Password, request.ConfirmPassword))
        {
            LastValidationError = "Passwords do not match.";
            return new CreateAccountResult(
                CreateAccountStatus.ValidationFailed,
                string.Empty,
                null,
                request.Username,
                request.IsEphemeral,
                [],
                LastValidationError);
        }

        // Create user
        string sid;
        try
        {
            sid = windowsAccountService.CreateLocalUser(request.Username, request.Password);
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return new CreateAccountResult(
                CreateAccountStatus.WindowsAccountCreationFailed,
                string.Empty,
                null,
                request.Username,
                request.IsEphemeral,
                [],
                ex.Message);
        }

        var previousAccount = appState.Database.GetAccount(sid)?.Clone();
        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = request.Username,
            PreviousAccount = previousAccount,
            HadPreviousAccount = previousAccount != null,
            PreviousSidName = appState.Database.SidNames.TryGetValue(sid, out var previousSidName)
                ? previousSidName
                : null,
            HadPreviousSidName = appState.Database.SidNames.ContainsKey(sid),
            PreviousFirewallSettings = previousAccount?.Firewall.IsDefault == false
                ? previousAccount.Firewall.Clone()
                : null,
            HadPreviousFirewallSettings = previousAccount?.Firewall.IsDefault == false
        };

        // From here on, user exists — collect errors but don't abort
        try
        {
            uiThreadInvoker.Invoke(() =>
            {
                sidNameCache.UpdateName(sid, $"{Environment.MachineName}\\{request.Username}");
                var entry = appState.Database.GetOrCreateAccount(sid);
                entry.DeleteAfterUtc = request.IsEphemeral ? DateTime.UtcNow.AddHours(24) : null;

                databaseService.SaveConfig(
                    appState.Database,
                    session.PinDerivedKey,
                    session.CredentialStore.ArgonSalt);
            });
        }
        catch (Exception ex)
        {
            return new CreateAccountResult(
                CreateAccountStatus.CleanupStateSaveFailed,
                sid,
                null,
                request.Username,
                request.IsEphemeral,
                [],
                ex.Message,
                rollbackState);
        }

        var errors = new List<string>();

        // Add to explicitly checked groups
        if (request.CheckedGroups.Count > 0)
        {
            try
            {
                groupMembership.AddUserToGroups(sid, request.Username,
                    request.CheckedGroups.Select(g => g.Sid).ToList());
            }
            catch (Exception ex)
            {
                errors.Add($"Group membership: {ex.Message}");
            }
        }

        // Remove from default groups (e.g. Users) when user explicitly unchecked them
        if (request.UncheckedGroups.Count > 0)
        {
            try
            {
                groupMembership.RemoveUserFromGroups(sid, request.Username,
                    request.UncheckedGroups.Select(g => g.Sid).ToList());
            }
            catch (Exception ex)
            {
                errors.Add($"Group removal: {ex.Message}");
            }
        }

        var canBlockLogon = request.AllowLogon || licenseService.CanHideAccount(request.CurrentHiddenCount);
        if (!request.AllowLogon && !canBlockLogon)
            errors.Add(licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, request.CurrentHiddenCount)!);

        var restrictionResult = restrictionCoordinator.ApplyRestrictions(
            sid,
            request.Username,
            logonBlocked: !request.AllowLogon && canBlockLogon,
            networkLoginBlocked: !request.AllowNetworkLogin,
            backgroundAutorunBlocked: !request.AllowBgAutorun);
        foreach (var entry in restrictionResult.Entries.Where(e => e.Status != AccountRestrictionStatus.Succeeded))
            errors.Add(AccountRestrictionEntryFormatter.Format(entry));

        // Build CreatedPassword as independent copy (caller owns disposal)
        var password = request.Password.Copy();

        return new CreateAccountResult(
            CreateAccountStatus.Succeeded,
            sid,
            password,
            request.Username,
            request.IsEphemeral,
            errors,
            RollbackState: rollbackState,
            RestrictionEntries: restrictionResult.Entries);
    }
}

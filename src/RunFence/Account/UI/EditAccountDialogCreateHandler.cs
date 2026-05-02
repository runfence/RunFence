using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.UI;

namespace RunFence.Account.UI;

public class EditAccountDialogCreateHandler(
    IWindowsAccountService windowsAccountService,
    ILocalGroupMembershipService groupMembership,
    IAccountLoginRestrictionService loginRestriction,
    IAccountLsaRestrictionService lsaRestriction,
    ILicenseService licenseService)
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

    public record CreateAccountResult(
        string Sid,
        ProtectedString Password,
        string Username,
        bool IsEphemeral,
        List<string> Errors);

    /// <summary>
    /// Set after Execute returns null — describes the validation error to show in the status label.
    /// </summary>
    public string? LastValidationError { get; private set; }

    /// <summary>
    /// Creates a local Windows user account with the specified settings.
    /// Returns null if validation fails; check <see cref="LastValidationError"/> for the message.
    /// On success, returns a result with SID, password, username, and any non-fatal errors.
    /// </summary>
    public CreateAccountResult? Execute(CreateAccountRequest request)
    {
        LastValidationError = null;

        // Validate username
        if (request.Username.Length is 0 or > 20)
        {
            LastValidationError = "Account name must be 1\u201320 characters.";
            return null;
        }

        if (request.Username.IndexOfAny(EditAccountDialog.InvalidNameChars) >= 0)
        {
            LastValidationError = "Account name contains invalid characters.";
            return null;
        }

        if (request.Password.Length > 0 && !ProtectedString.ContentEqual(request.Password, request.ConfirmPassword))
        {
            LastValidationError = "Passwords do not match.";
            return null;
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
            return null;
        }

        // From here on, user exists — collect errors but don't abort
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

        // Network Login (unchecked = local only)
        if (!request.AllowNetworkLogin)
        {
            try
            {
                lsaRestriction.SetLocalOnlyBySid(sid, true);
            }
            catch (Exception ex)
            {
                errors.Add($"Network Login: {ex.Message}");
            }
        }

        // Logon (unchecked = blocked)
        if (!request.AllowLogon)
        {
            if (!licenseService.CanHideAccount(request.CurrentHiddenCount))
            {
                errors.Add(licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, request.CurrentHiddenCount)!);
            }
            else
            {
                try
                {
                    loginRestriction.SetLoginBlockedBySid(sid, request.Username, true);
                }
                catch (Exception ex)
                {
                    errors.Add($"Logon: {ex.Message}");
                }
            }
        }

        // Bg Autorun (unchecked = no bg autostart)
        if (!request.AllowBgAutorun)
        {
            try
            {
                lsaRestriction.SetNoBgAutostartBySid(sid, true);
            }
            catch (Exception ex)
            {
                errors.Add($"Bg Autorun: {ex.Message}");
            }
        }

        // Build CreatedPassword as independent copy (caller owns disposal)
        var password = request.Password.Copy();

        return new CreateAccountResult(sid, password, request.Username, request.IsEphemeral, errors);
    }
}
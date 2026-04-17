using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

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
        string PasswordText,
        string ConfirmPasswordText,
        bool IsEphemeral,
        List<(string Sid, string Name)> CheckedGroups,
        List<(string Sid, string Name)> UncheckedGroups,
        bool AllowLogon,
        bool AllowNetworkLogin,
        bool AllowBgAutorun,
        int CurrentHiddenCount);

    public record CreateAccountResult(
        string Sid,
        SecureString Password,
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

        if (request.PasswordText.Length > 0 && request.PasswordText != request.ConfirmPasswordText)
        {
            LastValidationError = "Passwords do not match.";
            return null;
        }

        // Create user
        string sid;
        try
        {
            sid = windowsAccountService.CreateLocalUser(request.Username, request.PasswordText);
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

        // Build CreatedPassword SecureString (caller owns disposal)
        var password = new SecureString();
        foreach (char c in request.PasswordText)
            password.AppendChar(c);
        password.MakeReadOnly();

        return new CreateAccountResult(sid, password, request.Username, request.IsEphemeral, errors);
    }
}
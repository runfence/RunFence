using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Encapsulates the OS mutation logic for saving an existing account's edit:
/// rename, group changes, and account restrictions.
/// Used exclusively by <see cref="EditAccountDialog"/> in edit mode.
/// </summary>
public class EditAccountDialogSaveHandler(
    IWindowsAccountService windowsAccountService,
    ILocalGroupMembershipService groupMembership,
    IAccountLoginRestrictionService loginRestriction,
    IAccountLsaRestrictionService lsaRestriction,
    IAccountValidationService accountValidation,
    ILicenseService licenseService)
{
    public record SaveAccountRequest(
        string Sid,
        string CurrentUsername,
        string NewName,
        List<string> GroupsToAdd,
        List<string> GroupsToRemove,
        string? AdminGroupSid,
        /// <summary>New value to apply, or null if unchanged.</summary>
        bool? NewNetworkLogin,
        /// <summary>New value to apply, or null if unchanged.</summary>
        bool? NewLogon,
        /// <summary>New value to apply, or null if unchanged.</summary>
        bool? NewBgAutorun,
        int CurrentHiddenCount,
        bool? NoLogonState);

    public record SaveAccountResult(
        /// <summary>Validation error that prevented save — show in status label, re-enable OK button.</summary>
        string? ValidationError,
        /// <summary>New username if renamed, or null if unchanged.</summary>
        string? NewUsername,
        /// <summary>Non-fatal errors from individual OS mutations.</summary>
        List<string> Errors);

    /// <summary>
    /// Applies OS mutations for an edit-mode save. Validates admin group removal first.
    /// Returns a result; check <see cref="SaveAccountResult.ValidationError"/> to determine
    /// if the dialog should stay open.
    /// </summary>
    public SaveAccountResult Execute(SaveAccountRequest request)
    {
        // Validate admin group removal before any OS mutations
        if (request.AdminGroupSid != null && request.GroupsToRemove.Contains(request.AdminGroupSid))
        {
            try
            {
                accountValidation.ValidateNotCurrentAccount(request.Sid, "remove from Administrators");
                accountValidation.ValidateNotLastAdmin(request.Sid, "remove from Administrators");
            }
            catch (InvalidOperationException ex)
            {
                return new SaveAccountResult(ex.Message, null, []);
            }
        }

        var errors = new List<string>();
        var effectiveName = request.CurrentUsername;

        // Rename if changed
        var nameChanged = !string.Equals(request.NewName, request.CurrentUsername, StringComparison.OrdinalIgnoreCase);
        if (nameChanged)
        {
            try
            {
                windowsAccountService.RenameAccount(request.Sid, request.CurrentUsername, request.NewName);
                effectiveName = request.NewName;
            }
            catch (Exception ex)
            {
                return new SaveAccountResult(ex.Message, null, []);
            }
        }

        // Note: OS mutations below (group changes, restrictions) are applied as independent operations.
        // Partial success is by design — failures are collected in Errors and shown to the user after
        // the dialog closes, rather than rolling back successful operations. Rollback would require
        // tracking every change made, which adds complexity with little practical benefit given that
        // these are non-destructive OS settings that can be manually corrected.
        if (request.GroupsToAdd.Count > 0)
        {
            try
            {
                groupMembership.AddUserToGroups(request.Sid, effectiveName, request.GroupsToAdd);
            }
            catch (Exception ex)
            {
                errors.Add($"Group add: {ex.Message}");
            }
        }

        if (request.GroupsToRemove.Count > 0)
        {
            try
            {
                groupMembership.RemoveUserFromGroups(request.Sid, effectiveName, request.GroupsToRemove);
            }
            catch (Exception ex)
            {
                errors.Add($"Group remove: {ex.Message}");
            }
        }

        // Restrictions — only apply if explicitly changed from initial state (null = not changed).
        if (request.NewNetworkLogin.HasValue)
        {
            try
            {
                lsaRestriction.SetLocalOnlyBySid(request.Sid, localOnly: !request.NewNetworkLogin.Value);
            }
            catch (Exception ex)
            {
                errors.Add($"Network Login: {ex.Message}");
            }
        }

        if (request.NewLogon.HasValue)
        {
            var settingBlocked = !request.NewLogon.Value;
            if (settingBlocked && request.NoLogonState != true
                               && !licenseService.CanHideAccount(request.CurrentHiddenCount))
            {
                errors.Add(licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, request.CurrentHiddenCount)!);
            }
            else
            {
                try
                {
                    loginRestriction.SetLoginBlockedBySid(request.Sid, effectiveName, blocked: settingBlocked);
                }
                catch (Exception ex)
                {
                    errors.Add($"Logon: {ex.Message}");
                }
            }
        }

        if (request.NewBgAutorun.HasValue)
        {
            try
            {
                lsaRestriction.SetNoBgAutostartBySid(request.Sid, blocked: !request.NewBgAutorun.Value);
            }
            catch (Exception ex)
            {
                errors.Add($"Bg Autorun: {ex.Message}");
            }
        }

        return new SaveAccountResult(null, nameChanged ? effectiveName : null, errors);
    }
}
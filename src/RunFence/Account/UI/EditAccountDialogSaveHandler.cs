using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

public class EditAccountDialogSaveHandler(
    IWindowsAccountService windowsAccountService,
    ILocalGroupMutationService groupMembership,
    IAccountLsaRestrictionService lsaRestriction,
    IAccountRestrictionCoordinator restrictionCoordinator,
    IAccountValidationService accountValidation,
    ILicenseService licenseService)
{
    public enum SaveAccountStatus
    {
        Saved,
        SavedWithWarnings,
        ValidationFailed
    }

    public record SaveAccountRequest(
        string Sid,
        string CurrentUsername,
        string NewName,
        List<string> GroupsToAdd,
        List<string> GroupsToRemove,
        string? AdminGroupSid,
        bool? NewNetworkLogin,
        bool? NewLogon,
        bool? NewBgAutorun,
        int CurrentHiddenCount,
        bool? NoLogonState);

    public record SaveAccountResult(
        SaveAccountStatus Status,
        string? ValidationError,
        string? NewUsername,
        List<string> Errors);

    public SaveAccountResult Execute(SaveAccountRequest request)
    {
        if (request.AdminGroupSid != null && request.GroupsToRemove.Contains(request.AdminGroupSid))
        {
            try
            {
                accountValidation.ValidateNotCurrentAccount(request.Sid, "remove from Administrators");
                accountValidation.ValidateNotLastAdmin(request.Sid, "remove from Administrators");
            }
            catch (InvalidOperationException ex)
            {
                return new SaveAccountResult(SaveAccountStatus.ValidationFailed, ex.Message, null, []);
            }
        }

        var errors = new List<string>();
        var effectiveName = request.CurrentUsername;
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
                return new SaveAccountResult(SaveAccountStatus.ValidationFailed, ex.Message, null, []);
            }
        }

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

        var hasRestrictionChange = request.NewNetworkLogin.HasValue || request.NewLogon.HasValue || request.NewBgAutorun.HasValue;
        if (hasRestrictionChange)
        {
            var currentNetworkAllowed = lsaRestriction.GetLocalOnlyState(request.Sid) != true;
            var currentBgAutorunAllowed = lsaRestriction.GetNoBgAutostartState(request.Sid) != true;
            var currentLogonAllowed = request.NoLogonState != true;
            var targetLogonAllowed = request.NewLogon ?? currentLogonAllowed;
            var canBlockLogon = targetLogonAllowed || request.NoLogonState == true ||
                                licenseService.CanHideAccount(request.CurrentHiddenCount);

            if (!targetLogonAllowed && !canBlockLogon)
            {
                errors.Add(licenseService.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, request.CurrentHiddenCount)!);
            }

            var restrictionResult = restrictionCoordinator.ApplyRestrictions(
                request.Sid,
                effectiveName,
                logonBlocked: !targetLogonAllowed && canBlockLogon,
                networkLoginBlocked: !(request.NewNetworkLogin ?? currentNetworkAllowed),
                backgroundAutorunBlocked: !(request.NewBgAutorun ?? currentBgAutorunAllowed));
            foreach (var entry in restrictionResult.Entries.Where(e => e.Status != AccountRestrictionStatus.Succeeded))
                errors.Add(AccountRestrictionEntryFormatter.Format(entry));
        }

        var status = errors.Count == 0 ? SaveAccountStatus.Saved : SaveAccountStatus.SavedWithWarnings;
        return new SaveAccountResult(status, null, nameChanged ? effectiveName : null, errors);
    }
}

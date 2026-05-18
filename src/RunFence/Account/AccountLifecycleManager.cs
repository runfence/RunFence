using Microsoft.Win32;
using RunFence.Account.OrphanedProfiles;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

public class AccountLifecycleManager(
    IWindowsAccountService windowsAccountService,
    IAccountLoginRestrictionService loginRestriction,
    IAccountLsaRestrictionService lsaRestriction,
    IGroupPolicyScriptHelper gpHelper,
    IOrphanedProfileService orphanedProfileService,
    IOrphanedAclCleanupService aclCleanupService,
    ILoggingService log,
    IAccountValidationService accountValidation,
    IProfilePathResolver profilePathResolver,
    ValidationRunner validationRunner)
    : IAccountLifecycleManager
{

    /// <summary>
    /// Validates that the account can be deleted (not interactive user, not last admin, no running processes).
    /// Returns structured validation data so callers can distinguish a hard block
    /// from the delete-specific "running processes can be killed first" path.
    /// </summary>
    public async Task<AccountDeleteValidationResult> ValidateDeleteAsync(string sid)
    {
        var errors = new List<string>();

        if (!validationRunner.RunValidation("ValidateNotInteractiveUser",
                () => accountValidation.ValidateNotInteractiveUser(sid, "delete"), errors))
            return new AccountDeleteValidationResult(errors[0], Array.Empty<ProcessInfo>());

        if (!await Task.Run(() => validationRunner.RunValidation("ValidateNotLastAdmin",
                () => accountValidation.ValidateNotLastAdmin(sid, "delete"), errors)))
            return new AccountDeleteValidationResult(errors[0], Array.Empty<ProcessInfo>());

        var runningProcesses = await Task.Run(() => accountValidation.GetRunningProcesses(sid));
        if (runningProcesses.Count > 0)
            return new AccountDeleteValidationResult(null, runningProcesses);

        return AccountDeleteValidationResult.Success;
    }

    /// <summary>
    /// Clears account restrictions (hidden, local-only, login block) before deletion.
    /// When <paramref name="settings"/> is provided and <see cref="AppSettings.OriginalUacAdminEnumeration"/>
    /// is set, the UAC admin enumeration registry value is reverted to its original state and cleared.
    /// Best-effort — failures are silently ignored.
    /// </summary>
    public void ClearAccountRestrictions(string sid, string username, AppSettings? settings = null)
    {
        try
        {
            loginRestriction.SetAccountHidden(username, sid, false);
        }
        catch
        {
        }

        try
        {
            lsaRestriction.SetLocalOnlyBySid(sid, false);
        }
        catch
        {
        }

        // Use gpHelper directly rather than SetLoginBlockedBySid: the latter contains a rollback
        // that re-enables the GP logon script if SetAccountHidden fails, which is wrong during
        // deletion. SetAccountHidden is already handled by the standalone call above.
        try
        {
            gpHelper.SetLoginBlocked(sid, false);
        }
        catch
        {
        }

        try
        {
            lsaRestriction.SetNoBgAutostartBySid(sid, false);
        }
        catch
        {
        }

        if (settings?.OriginalUacAdminEnumeration != null &&
            (settings.UacAdminEnumerationSid == null ||
             string.Equals(settings.UacAdminEnumerationSid, sid, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                RevertUacAdminEnumeration(settings);
            }
            catch
            {
            }
        }
    }

    private static void RevertUacAdminEnumeration(AppSettings settings)
    {
        var original = settings.OriginalUacAdminEnumeration;
        if (original == null)
            return;

        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\CredUI";
        if (original == -1)
        {
            // Original state was key absent — restore by deleting the value.
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue("EnumerateAdministrators", throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyPath);
            key.SetValue("EnumerateAdministrators", original.Value, RegistryValueKind.DWord);
        }

        settings.OriginalUacAdminEnumeration = null;
        settings.UacAdminEnumerationSid = null;
    }

    /// <summary>
    /// Deletes the Windows user account. Returns (success, errorMessage).
    /// Does NOT invalidate the local user cache — caller should do that
    /// after all post-deletion operations (credential removal, profile deletion) complete.
    /// </summary>
    public AccountDeletionResult DeleteSamAccount(string sid)
    {
        try
        {
            windowsAccountService.DeleteSamAccount(sid);
            return new AccountDeletionResult(true, sid, null);
        }
        catch (Exception ex)
        {
            return new AccountDeletionResult(false, sid, ex.Message);
        }
    }

    /// <summary>
    /// Deletes the user profile directory via OrphanedProfileService.
    /// Returns an error message if deletion failed, or null on success.
    /// </summary>
    public async Task<string?> DeleteProfileAsync(string sid)
    {
        var profilePath = profilePathResolver.TryGetProfilePath(sid);
        if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath))
            return null;

        var profile = new OrphanedProfile(sid, profilePath);
        var result = await Task.Run(() => orphanedProfileService.DeleteProfiles([profile]));
        if (result.Failed.Count > 0)
            return result.Failed[0].Error;

        return null;
    }

    public async Task<AclReferenceCleanupResult> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct)
    {
        try
        {
            var report = await aclCleanupService.CleanupAclReferencesAsync(sids, progress, ct);
            var fixedCount = report.Count(r => r.Error == null);
            return new AclReferenceCleanupResult(fixedCount, null);
        }
        catch (OperationCanceledException)
        {
            return new AclReferenceCleanupResult(0, null);
        }
        catch (Exception ex)
        {
            log.Error("Background ACL cleanup failed", ex);
            return new AclReferenceCleanupResult(0, ex.Message);
        }
    }
}

using Microsoft.Win32;
using RunFence.Account.OrphanedProfiles;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

public class AccountLifecycleManager(
    IWindowsAccountService windowsAccountService,
    IAccountRestrictionService accountRestriction,
    IOrphanedProfileService orphanedProfileService,
    IOrphanedAclCleanupService aclCleanupService,
    ILoggingService log,
    IAccountValidationService accountValidation,
    ISidResolver sidResolver)
    : IAccountLifecycleManager
{
    /// <summary>
    /// Validates that the account can be deleted (not interactive user, not last admin, no running processes).
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public async Task<string?> ValidateDeleteAsync(string sid)
    {
        try
        {
            accountValidation.ValidateNotInteractiveUser(sid, "delete");
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        try
        {
            accountValidation.ValidateNotLastAdmin(sid, "delete");
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        try
        {
            await Task.Run(() => accountValidation.ValidateNoRunningProcesses(sid, "delete"));
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
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
            accountRestriction.SetAccountHidden(username, sid, false);
        }
        catch
        {
        }

        try
        {
            accountRestriction.SetLocalOnlyBySid(sid, false);
        }
        catch
        {
        }

        try
        {
            accountRestriction.SetLoginBlockedBySid(sid, username, false);
        }
        catch
        {
        }

        try
        {
            accountRestriction.SetNoBgAutostartBySid(sid, false);
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
    public (bool Success, string? Error) DeleteUser(string sid)
    {
        try
        {
            windowsAccountService.DeleteUser(sid);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Deletes the user profile directory via OrphanedProfileService.
    /// Returns an error message if deletion failed, or null on success.
    /// </summary>
    public async Task<string?> DeleteProfileAsync(string sid)
    {
        var profilePath = sidResolver.TryGetProfilePath(sid);
        if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath))
            return null;

        var profile = new OrphanedProfile(sid, profilePath);
        var result = await Task.Run(() => orphanedProfileService.DeleteProfiles([profile]));
        if (result.Failed.Count > 0)
            return result.Failed[0].Error;

        return null;
    }

    public async Task<(int FixedCount, string? Error)> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct)
    {
        try
        {
            var report = await aclCleanupService.CleanupAclReferencesAsync(sids, progress, ct);
            var fixedCount = report.Count(r => r.Error == null);
            return (fixedCount, null);
        }
        catch (OperationCanceledException)
        {
            return (0, null); // expected when superseded
        }
        catch (Exception ex)
        {
            log.Error("Background ACL cleanup failed", ex);
            return (0, ex.Message);
        }
    }
}
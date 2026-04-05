using RunFence.Account.OrphanedProfiles;
using RunFence.Core.Models;

namespace RunFence.Account;

/// <summary>
/// Manages the lifecycle of local Windows accounts: validation, restrictions, deletion,
/// profile cleanup, and background ACL reference cleanup.
/// </summary>
public interface IAccountLifecycleManager
{
    /// <summary>
    /// Validates that the account can be deleted (not interactive user, not last admin, no running processes).
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    Task<string?> ValidateDeleteAsync(string sid);

    /// <summary>
    /// Clears account restrictions (hidden, local-only, login block) before deletion.
    /// When <paramref name="settings"/> is provided and <see cref="AppSettings.OriginalUacAdminEnumeration"/>
    /// is set, the UAC admin enumeration registry value is reverted to its original state.
    /// Best-effort — failures are silently ignored.
    /// </summary>
    void ClearAccountRestrictions(string sid, string username, AppSettings? settings = null);

    /// <summary>
    /// Deletes the Windows user account. Returns (success, errorMessage).
    /// Does NOT invalidate the local user cache — caller should do that
    /// after all post-deletion operations (credential removal, profile deletion) complete.
    /// </summary>
    (bool Success, string? Error) DeleteUser(string sid);

    /// <summary>
    /// Deletes the user profile directory via OrphanedProfileService.
    /// Returns an error message if deletion failed, or null on success.
    /// </summary>
    Task<string?> DeleteProfileAsync(string sid);

    Task<(int FixedCount, string? Error)> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct);
}
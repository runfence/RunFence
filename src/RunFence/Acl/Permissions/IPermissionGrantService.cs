using System.Security.AccessControl;
using RunFence.Core.Models;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Result returned by <see cref="IPermissionGrantService.EnsureAccess"/> and
/// <see cref="IPermissionGrantService.EnsureExeDirectoryAccess"/>, distinguishing between
/// whether an ACE was added for the account SID and whether the database was written.
/// </summary>
/// <param name="GrantAdded">
/// <c>true</c> if a new ACE was applied for <c>accountSid</c> on the target path itself
/// (not ancestor traverse grants). Use this to trigger quick-access pinning.
/// </param>
/// <param name="DatabaseModified">
/// <c>true</c> if the in-memory database was written (main grant tracked, traverse tracked,
/// or the interactive-user auto-grant was recorded). Use this to decide whether to save config.
/// </param>
public readonly record struct EnsureAccessResult(bool GrantAdded, bool DatabaseModified);

/// <summary>
/// High-level permission grant service that atomically applies an ACE, records the grant in
/// <see cref="AccountEntry.Grants"/>, and ensures traverse access on ancestor directories.
/// Use this instead of calling <see cref="IAclPermissionService.EnsureRights"/> + AddGrant separately.
/// </summary>
public interface IPermissionGrantService
{
    /// <summary>
    /// Ensures <paramref name="accountSid"/> has <paramref name="rights"/> on the exact
    /// <paramref name="path"/>. Records the grant in <see cref="AccountEntry.Grants"/> via the
    /// UI thread invoker (safe to call from background threads). Traverse ACEs on ancestor
    /// directories are always applied and tracked. For AppContainer SIDs (<c>S-1-15-2-*</c>),
    /// also grants the interactive user access to the same path.
    /// <para>
    /// <paramref name="confirm"/> is called with (path, accountSid) before granting; return
    /// <c>false</c> to skip this grant, or throw <see cref="OperationCanceledException"/> from
    /// inside the callback to abort. <c>null</c> = silent grant.
    /// </para>
    /// </summary>
    EnsureAccessResult EnsureAccess(string path, string accountSid, FileSystemRights rights,
        Func<string, string, bool>? confirm = null);

    /// <summary>
    /// Ensures <paramref name="accountSid"/> has ReadAndExecute on <paramref name="exePath"/>'s parent
    /// directory. When the exe is inside <see cref="AppContext.BaseDirectory"/>, the grant is silent;
    /// otherwise the provided <paramref name="confirm"/> callback is used.
    /// </summary>
    EnsureAccessResult EnsureExeDirectoryAccess(string exePath, string accountSid,
        Func<string, string, bool>? confirm = null);

    /// <summary>
    /// Records a grant with specific saved rights state in <see cref="AccountEntry.Grants"/> (no NTFS ACE application).
    /// Used by wizard Prepare System to track drive ACL changes that were applied directly via
    /// <see cref="System.Security.AccessControl.DirectorySecurity"/> rather than through this service.
    /// </summary>
    void RecordGrantWithRights(string path, string accountSid, SavedRightsState savedRights);
}
using System.Security.AccessControl;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Universal implementation of <see cref="IPermissionGrantService"/>. Works for any SID including
/// AppContainer SIDs — <see cref="IAclPermissionService.EnsureRights"/> and
/// <see cref="IUserTraverseService"/> both handle container SIDs correctly via
/// <see cref="IAclPermissionService.ResolveAccountGroupSids"/>.
/// </summary>
public class PermissionGrantService(
    IAclPermissionService aclPermission,
    IUserTraverseService userTraverseService,
    IDatabaseProvider databaseProvider,
    ILoggingService log,
    IInteractiveUserResolver interactiveUserResolver,
    IUiThreadInvoker uiThreadInvoker) : IPermissionGrantService
{
    public EnsureAccessResult EnsureAccess(string path, string accountSid, FileSystemRights rights,
        Func<string, string, bool>? confirm = null)
    {
        bool userDeclined = false;

        Func<string, bool>? wrapped = confirm != null
            ? p =>
            {
                bool r = confirm(p, accountSid);
                if (!r)
                    userDeclined = true;
                return r;
            }
            : null;

        bool aceAdded = aclPermission.EnsureRights(path, accountSid, rights, log, wrapped);

        if (aceAdded)
        {
            var database = databaseProvider.GetDatabase();
            uiThreadInvoker.RunOnUiThread(() => AccountGrantHelper.AddGrant(database, accountSid, path));
        }

        bool traverseAdded = false;
        if (!userDeclined)
        {
            // When path is itself a directory, ensure traverse on that directory (so the account
            // can enter it). When path is a file, ensure traverse on the containing directory.
            var traverseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(traverseDir))
                (traverseAdded, _) = userTraverseService.EnsureTraverseAccess(accountSid, traverseDir);
        }

        // Container auto-grant: for AppContainer SIDs also grant the interactive desktop user.
        // IsBlockedAclRoot is intentionally NOT checked here — that validation applies only
        // to app entry ACL root paths and is enforced at UI entry time (AclConfigSection).
        // Skip if the interactive user already has the necessary effective rights on the path.
        EnsureAccessResult interactiveResult = default;
        if (!userDeclined && accountSid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase))
        {
            var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
            if (!string.IsNullOrEmpty(interactiveSid) && aclPermission.NeedsPermissionGrant(path, interactiveSid, rights))
                interactiveResult = EnsureAccess(path, interactiveSid, rights, confirm);
        }

        // GrantAdded: aceAdded only — intentionally excludes traverseAdded. Traverse grants are
        // on parent/ancestor directories, not the target path itself. Callers triggering
        // quick-access pinning act on the target path only.
        // DatabaseModified: whether the in-memory database was written (main grant, traverse, or
        // interactive auto-grant).
        return new EnsureAccessResult(
            GrantAdded: aceAdded,
            DatabaseModified: aceAdded || traverseAdded || interactiveResult.DatabaseModified);
    }

    public EnsureAccessResult EnsureExeDirectoryAccess(string exePath, string accountSid,
        Func<string, string, bool>? confirm = null)
    {
        var exeDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir))
            return default;
        bool isOwnExe = exePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
        return EnsureAccess(exeDir, accountSid, FileSystemRights.ReadAndExecute, isOwnExe ? null : confirm);
    }

    public void RecordGrantWithRights(string path, string accountSid, SavedRightsState savedRights)
    {
        var database = databaseProvider.GetDatabase();
        AccountGrantHelper.AddGrant(database, accountSid, path);
        var normalized = Path.GetFullPath(path);
        var grants = database.GetOrCreateAccount(accountSid).Grants;
        var entry = grants.LastOrDefault(g =>
            string.Equals(g.Path, normalized, StringComparison.OrdinalIgnoreCase) && !g.IsTraverseOnly);
        if (entry != null)
            entry.SavedRights = savedRights;
    }

    /// <summary>
    /// Converts a <c>Func&lt;string, bool?&gt;</c> confirm callback (where null = cancel) to the
    /// <c>Func&lt;string, string, bool&gt;</c> format expected by <see cref="EnsureAccess"/>.
    /// A null return from the input callback throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static Func<string, string, bool> AdaptConfirm(Func<string, bool?> confirm)
        => (path, _) => confirm(path) ?? throw new OperationCanceledException();
}
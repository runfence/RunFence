using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Synchronizes ACL grants and traverse entries for the interactive user when container
/// grants change. When a container SID gains or loses a grant, the interactive user SID
/// must receive the same grant so the desktop user token can reach the path.
/// </summary>
public class ContainerInteractiveUserSync(
    GrantCoreOperations grantCore,
    TraverseCoreOperations traverseCore,
    IInteractiveUserResolver interactiveUserResolver,
    IAclPermissionService aclPermission,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log)
{
    /// <summary>
    /// Adds a matching allow grant for the interactive user when the container gains a new
    /// allow grant, but only if the interactive user currently lacks sufficient rights.
    /// </summary>
    public GrantOperationResult SyncAllowGrantToInteractiveUser(string containerSid, string path,
        SavedRightsState rights)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return default;

        bool isFolder = Directory.Exists(path);
        var fsRights = GrantRightsMapper.MapAllowRights(rights, isFolder);
        if (!aclPermission.NeedsPermissionGrant(path, iuSid, fsRights))
            return default;

        return AddGrantForInteractiveUser(containerSid, iuSid, path, rights);
    }

    /// <summary>
    /// Removes the interactive user's matching grant for <paramref name="path"/> when
    /// <paramref name="containerSid"/> loses its grant, provided no other container still
    /// needs that path.
    /// </summary>
    public void RevertInteractiveUserGrant(string containerSid, string path,
        SavedRightsState? containerRights)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var shouldRevert = dbAccessor.Read(db =>
        {
            var iuEntry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, path, isDeny: false);
            if (iuEntry == null) return false;
            // Only revoke grants that were created by this container (or legacy null entries
            // for backward compatibility). User-added IU grants (OwnerContainerSid != containerSid
            // and != null) are preserved.
            if (iuEntry.OwnerContainerSid != null &&
                !string.Equals(iuEntry.OwnerContainerSid, containerSid, StringComparison.OrdinalIgnoreCase))
                return false;
            if (iuEntry.SavedRights != containerRights) return false;
            return !AnyOtherContainerNeedsPath(db, containerSid, path);
        });

        if (!shouldRevert)
            return;

        grantCore.RemoveGrant(iuSid, path, isDeny: false, updateFileSystem: true);
        traverseCore.CleanupOrphanedTraverse(iuSid, path);
    }

    /// <summary>
    /// Removes the interactive user's traverse entry for <paramref name="path"/> when
    /// <paramref name="containerSid"/> loses its traverse, provided no other container still
    /// needs that path.
    /// </summary>
    public void RevertInteractiveUserTraverse(string containerSid, string path)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var shouldRemove = dbAccessor.Read(db => !AnyOtherContainerNeedsPath(db, containerSid, path));

        if (!shouldRemove)
            return;

        traverseCore.RemoveTraverse(iuSid, path, updateFileSystem: true);
    }

    /// <summary>
    /// Removes interactive user grants and traverse entries for all paths in
    /// <paramref name="containerGrants"/> when <paramref name="containerSid"/> is fully removed,
    /// provided no other container still needs each path.
    /// </summary>
    public void RevertAllInteractiveUserGrants(string containerSid,
        IReadOnlyList<GrantedPathEntry> containerGrants, bool updateFileSystem)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;

        var pathsToRevert = dbAccessor.Read(db =>
        {
            var result = new List<string>();
            foreach (var ce in containerGrants.Where(e => !e.IsDeny && !e.IsTraverseOnly))
            {
                var iuEntry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, ce.Path, isDeny: false);
                if (iuEntry == null) continue;
                // Only revoke grants owned by this container (or legacy null entries).
                if (iuEntry.OwnerContainerSid != null &&
                    !string.Equals(iuEntry.OwnerContainerSid, containerSid, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (iuEntry.SavedRights != ce.SavedRights) continue;
                if (AnyOtherContainerNeedsPath(db, containerSid, ce.Path)) continue;
                result.Add(ce.Path);
            }
            return result;
        });

        foreach (var revertPath in pathsToRevert)
        {
            try
            {
                grantCore.RemoveGrant(iuSid, revertPath, isDeny: false,
                    updateFileSystem: updateFileSystem);
                traverseCore.CleanupOrphanedTraverse(iuSid, revertPath);
            }
            catch (Exception ex)
            {
                log.Warn(
                    $"RevertAllInteractiveUserGrants: failed to revert grant on '{revertPath}' for IU '{iuSid}': {ex.Message}");
            }
        }
    }

    private GrantOperationResult AddGrantForInteractiveUser(string containerSid, string iuSid,
        string path, SavedRightsState rights)
    {
        var coreResult = grantCore.AddGrant(iuSid, path, isDeny: false, rights, ownerSid: null);

        if (!coreResult.AlreadyExisted)
        {
            // Tag the newly created entry as owned by this container so RevertInteractiveUserGrant
            // can distinguish it from user-added IU grants that happen to cover the same path.
            dbAccessor.Write(db =>
            {
                var entry = GrantCoreOperations.FindGrantEntryInDb(db, iuSid, Path.GetFullPath(path), isDeny: false);
                if (entry != null)
                    entry.OwnerContainerSid = containerSid;
            });

            bool isFolder = Directory.Exists(path);
            var traverseDir = isFolder ? path : Path.GetDirectoryName(path);
            bool traverseAdded = false;
            if (!string.IsNullOrEmpty(traverseDir))
            {
                var (modified, _) = traverseCore.AddTraverse(iuSid, traverseDir);
                traverseAdded = modified;
            }

            return new GrantOperationResult(
                GrantAdded: true,
                TraverseAdded: traverseAdded,
                DatabaseModified: true);
        }

        return new GrantOperationResult(
            GrantAdded: false,
            TraverseAdded: false,
            DatabaseModified: coreResult.DatabaseModified);
    }

    private static bool AnyOtherContainerNeedsPath(AppDatabase database, string excludeContainerSid,
        string path)
        => database.Accounts.Any(acct =>
            AclHelper.IsContainerSid(acct.Sid) &&
            !string.Equals(acct.Sid, excludeContainerSid, StringComparison.OrdinalIgnoreCase) &&
            acct.Grants.Any(e =>
                !e.IsTraverseOnly && !e.IsDeny &&
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));

}

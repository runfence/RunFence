using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Manages automatic traverse-entry creation and removal in response to allow-grant
/// add/remove/mode-switch operations in the ACL Manager.
/// </summary>
public class TraverseAutoManager(IAclPermissionService aclPermission, IDatabaseProvider databaseProvider)
{
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private IReadOnlyList<string> _groupSids = null!;

    public void Initialize(AclManagerPendingChanges pending, string sid, IReadOnlyList<string> groupSids)
    {
        _pending = pending;
        _sid = sid;
        _groupSids = groupSids;
    }

    /// <summary>
    /// Returns the traverse path for a grant at <paramref name="grantPath"/>:
    /// the grant path itself when it is a directory (folder grant), or its parent directory
    /// otherwise (file grant). Returns null when the parent directory cannot be determined.
    /// </summary>
    public static string? GetTraversePath(string grantPath)
        => Directory.Exists(grantPath) ? grantPath : Path.GetDirectoryName(grantPath);

    /// <summary>
    /// Auto-adds a traverse entry for <paramref name="traversePath"/> to PendingTraverseAdds
    /// if none already exists in the DB or in pending state.
    /// </summary>
    public void AutoAddTraverseIfMissing(string traversePath)
    {
        // Already pending add? Nothing more to do.
        if (_pending.IsPendingTraverseAdd(traversePath))
            return;

        // If it was pending removal, cancel that removal — the allow grant depends on it.
        if (_pending.IsPendingTraverseRemove(traversePath))
        {
            _pending.PendingTraverseRemoves.Remove(traversePath);
            return;
        }

        // Already in DB as traverse-only? Already covered, nothing to add.
        if (HasExistingTraverseEntry(traversePath))
            return;


        // Skip auto-add when the SID already has effective traverse rights on this path.
        if (TraverseRightsHelper.HasEffectiveTraverse(traversePath, _sid, _groupSids, aclPermission))
            return;

        _pending.PendingTraverseAdds[traversePath] = new GrantedPathEntry
        {
            Path = traversePath,
            IsTraverseOnly = true
        };
    }

    /// <summary>
    /// Auto-removes the traverse entry for <paramref name="traversePath"/> if no remaining
    /// allow grants depend on it. "Remaining" = DB allow grants (excluding PendingRemoves)
    /// + PendingAdds allow grants, minus this path itself.
    /// </summary>
    public void AutoRemoveTraverseIfUnneeded(string traversePath)
    {
        if (!OtherAllowGrantsDependOnPath(traversePath))
        {
            if (_pending.IsPendingTraverseAdd(traversePath))
            {
                _pending.PendingTraverseAdds.Remove(traversePath);
                _pending.PendingTraverseConfigMoves.Remove(traversePath);
            }
            else if (HasExistingTraverseEntry(traversePath))
            {
                var traverseEntry = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants
                    .FirstOrDefault(e => e.IsTraverseOnly &&
                                         string.Equals(e.Path, traversePath, StringComparison.OrdinalIgnoreCase));
                if (traverseEntry != null)
                {
                    _pending.PendingTraverseRemoves[traversePath] = traverseEntry;
                    _pending.PendingTraverseConfigMoves.Remove(traversePath);
                }
            }
        }
    }

    private bool HasExistingTraverseEntry(string path)
    {
        return databaseProvider.GetDatabase().GetAccount(_sid)?.Grants
            .Any(e => e.IsTraverseOnly && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// Returns true if any allow grant (in DB or PendingAdds, excluding PendingRemoves and entries
    /// pending a mode switch to Deny) depends on <paramref name="path"/> as its traverse path.
    /// A grant depends on a traverse path when <see cref="GetTraversePath"/> of the grant
    /// equals <paramref name="path"/> (i.e., the grant is a folder grant at that path, or
    /// a file grant whose parent directory equals that path).
    /// </summary>
    private bool OtherAllowGrantsDependOnPath(string path)
    {
        // DB allow grants excluding pending removes and entries pending switch to Deny.
        var dbEntries = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        if (dbEntries != null)
        {
            foreach (var e in dbEntries)
            {
                if (e.IsDeny || e.IsTraverseOnly)
                    continue;
                if (_pending.IsPendingRemove(e.Path, e.IsDeny))
                    continue;
                // An entry pending a mode switch to Deny no longer depends on an allow traverse path.
                if (_pending.PendingModifications.TryGetValue((e.Path, e.IsDeny), out var mod) && mod.NewIsDeny)
                    continue;
                var effectivePath = GetTraversePath(e.Path);
                if (string.Equals(effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Pending add allow grants.
        foreach (var (key, e) in _pending.PendingAdds)
        {
            if (key.IsDeny)
                continue;
            var effectivePath = GetTraversePath(e.Path);
            if (string.Equals(effectivePath, path, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
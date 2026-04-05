using RunFence.Core.Models;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Helper for adding path grants to an account's <see cref="AccountEntry.Grants"/> list.
/// </summary>
public static class AccountGrantHelper
{
    /// <summary>
    /// Adds a non-traverse, non-duplicate Allow (or Deny) grant entry for <paramref name="sid"/>
    /// to the database account's grants. Normalizes the path and skips if already present.
    /// </summary>
    public static void AddGrant(AppDatabase db, string sid, string path, bool isDeny = false)
    {
        var entries = db.GetOrCreateAccount(sid).Grants;
        var normalized = Path.GetFullPath(path);
        if (!entries.Any(e =>
                string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) &&
                e.IsDeny == isDeny && !e.IsTraverseOnly))
            entries.Add(new GrantedPathEntry { Path = normalized, IsDeny = isDeny });
    }
}
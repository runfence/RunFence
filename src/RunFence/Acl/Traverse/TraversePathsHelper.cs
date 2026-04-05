using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Pure AccountEntry grants list helpers shared by all traverse-aware services.
/// Contains no ACE-granting logic — see <see cref="AncestorTraverseGranter"/> for ACE operations.
/// </summary>
public static class TraversePathsHelper
{
    /// <summary>
    /// Adds <paramref name="path"/> to <paramref name="traversePaths"/> if not already present.
    /// Returns true if newly added.
    /// </summary>
    public static bool TrackPath(List<GrantedPathEntry> traversePaths, string path, List<string> appliedPaths)
    {
        var normalized = Path.GetFullPath(path);
        if (traversePaths.Any(e =>
                e.IsTraverseOnly &&
                string.Equals(Path.GetFullPath(e.Path), normalized, StringComparison.OrdinalIgnoreCase)))
            return false;

        traversePaths.Add(new GrantedPathEntry
        {
            Path = normalized,
            IsTraverseOnly = true,
            AllAppliedPaths = appliedPaths.Count > 0 ? appliedPaths : null
        });
        return true;
    }

    public static void CollectPaths(GrantedPathEntry entry, HashSet<string> result)
    {
        if (entry.AllAppliedPaths != null)
            result.UnionWith(entry.AllAppliedPaths);
        else
            CollectAncestorPaths(entry.Path, result);
    }

    public static void CollectAncestorPaths(string path, HashSet<string> result)
    {
        var current = new DirectoryInfo(path);
        while (current != null)
        {
            result.Add(current.FullName);
            current = current.Parent;
        }
    }

    public static List<GrantedPathEntry> GetOrCreateTraversePaths(AppDatabase database, string sid)
        => database.GetOrCreateAccount(sid).Grants;

    public static List<GrantedPathEntry> GetTraversePaths(AppDatabase database, string sid)
        => database.GetAccount(sid)?.Grants ?? [];
}
using RunFence.Core.Models;
using RunFence.Acl;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Pure AccountEntry grants list helpers shared by all traverse-aware services.
/// Contains no ACE-granting logic — see <see cref="AncestorTraverseGranter"/> for ACE operations.
/// </summary>
public static class TraversePathsHelper
{
    /// <summary>
    /// Adds or refreshes <paramref name="path"/> in <paramref name="traversePaths"/>.
    /// Returns true when the stored entry changed.
    /// </summary>
    public static bool TrackPath(
        List<GrantedPathEntry> traversePaths,
        string path,
        List<string> appliedPaths,
        string? trackedSourceSid = null)
    {
        var normalized = Path.GetFullPath(path);
        var existing = traversePaths.FirstOrDefault(e =>
            e.IsTraverseOnly &&
            string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var trackedSource = trackedSourceSid ?? string.Empty;
            if (!AclHelper.IsSpecificContainerSid(trackedSource))
                return RefreshAppliedPaths(existing, appliedPaths);

            var sourceSids = existing.SourceSids;
            if (sourceSids == null)
                return false;
            bool changed = RefreshAppliedPaths(existing, appliedPaths);
            if (sourceSids.Contains(trackedSource, StringComparer.OrdinalIgnoreCase))
                return changed;
            sourceSids.Add(trackedSource);
            return true;
        }

        var entry = new GrantedPathEntry
        {
            Path = normalized,
            IsTraverseOnly = true,
            AllAppliedPaths = appliedPaths.Count > 0 ? appliedPaths.ToList() : null
        };
        if (AclHelper.IsSpecificContainerSid(trackedSourceSid ?? string.Empty))
            entry.SourceSids = [trackedSourceSid!];
        traversePaths.Add(entry);
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

    private static bool RefreshAppliedPaths(GrantedPathEntry existing, List<string> appliedPaths)
    {
        var refreshedAppliedPaths = appliedPaths.Count > 0 ? appliedPaths.ToList() : null;
        if (existing.AllAppliedPaths == null || refreshedAppliedPaths == null)
        {
            if (existing.AllAppliedPaths == refreshedAppliedPaths)
                return false;
        }
        else if (existing.AllAppliedPaths.SequenceEqual(refreshedAppliedPaths, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        existing.AllAppliedPaths = refreshedAppliedPaths;
        return true;
    }

}

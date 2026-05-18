using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

public sealed class TraverseGrantStateService(
    UiThreadDatabaseAccessor dbAccessor,
    IFileSystemPathInfo pathInfo,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator)
{
    public sealed record StoreSnapshot(
        IGrantIntentStore Store,
        IReadOnlyList<GrantedPathEntry> Entries);

    public IReadOnlyList<StoreSnapshot> CaptureStoreSnapshots(
        string ownerSid,
        string normalizedPath,
        IEnumerable<IGrantIntentStore> stores)
        => stores
            .Distinct()
            .Select(store => new StoreSnapshot(
                store,
                store.GetEntries(ownerSid)
                    .Where(entry =>
                        entry.IsTraverseOnly &&
                        string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Clone())
                    .ToList()))
            .ToList();

    public void RestoreStoreSnapshots(
        string ownerSid,
        string normalizedPath,
        IReadOnlyList<StoreSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var currentEntries = snapshot.Store.GetEntries(ownerSid)
                .Where(entry =>
                    entry.IsTraverseOnly &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in currentEntries)
                snapshot.Store.RemoveEntry(ownerSid, entry);

            foreach (var entry in snapshot.Entries)
                snapshot.Store.AddEntry(ownerSid, entry.Clone());
        }
    }

    public List<GrantedPathEntry> GetRemainingTraverseEntriesForCleanup(
        string sid,
        IReadOnlyList<GrantIntentLocation> removingLocations)
    {
        var removingPaths = removingLocations
            .Select(location => location.Entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return dbAccessor.Read(db =>
            traverseIntentStoreCoordinator.GetTraverseStoreOrEmpty(db, sid)
                .Where(entry => entry.IsTraverseOnly)
                .Select(entry =>
                {
                    if (!removingPaths.Contains(entry.Path))
                        return entry.Clone();

                    if (!AclHelper.IsSpecificContainerSid(sid))
                        return null;

                    if (entry.SourceSids == null ||
                        !entry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
                    {
                        return entry.Clone();
                    }

                    var remainingSources = entry.SourceSids
                        .Where(sourceSid => !string.Equals(sourceSid, sid, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (remainingSources.Count == 0)
                        return null;

                    var replacement = entry.Clone();
                    replacement.SourceSids = remainingSources;
                    return replacement;
                })
                .Where(entry => entry != null)
                .Select(entry => entry!)
                .ToList());
    }

    public HashSet<string> GetTraverseGrantPathsForCleanup(
        string sid,
        IReadOnlyList<GrantIntentLocation> removingLocations)
    {
        var removingPaths = removingLocations
            .Where(location => !location.Entry.IsTraverseOnly && !location.Entry.IsDeny)
            .Select(location => Path.GetFullPath(location.Entry.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return dbAccessor.Read(db =>
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var account in traverseIntentStoreCoordinator.GetGrantOwnersForTraverseCleanup(db, sid))
            {
                foreach (var grant in account.Grants.Where(grant => grant is { IsTraverseOnly: false, IsDeny: false }))
                {
                    var normalizedGrantPath = Path.GetFullPath(grant.Path);
                    if (string.Equals(account.Sid, sid, StringComparison.OrdinalIgnoreCase) &&
                        removingPaths.Contains(normalizedGrantPath))
                    {
                        continue;
                    }

                    var directory = pathInfo.DirectoryExists(normalizedGrantPath)
                        ? normalizedGrantPath
                        : Path.GetDirectoryName(normalizedGrantPath);
                    if (!string.IsNullOrEmpty(directory))
                        paths.Add(directory);
                }
            }

            return paths;
        });
    }

    public List<string> CollectStoredTraversePaths(GrantedPathEntry entry)
    {
        if (entry.AllAppliedPaths != null)
            return entry.AllAppliedPaths.ToList();

        var paths = new List<string>();
        var current = new DirectoryInfo(entry.Path);
        while (current != null)
        {
            paths.Add(current.FullName);
            current = current.Parent;
        }

        return paths;
    }

    public List<string> CollectStoredTraversePaths(IEnumerable<GrantedPathEntry> entries)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            foreach (var path in CollectStoredTraversePaths(entry))
            {
                if (seen.Add(path))
                    paths.Add(path);
            }
        }

        return paths;
    }

    public bool EntriesEquivalent(GrantedPathEntry left, GrantedPathEntry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) &&
               left.IsDeny == right.IsDeny &&
               left.IsTraverseOnly == right.IsTraverseOnly &&
               Equals(left.SavedRights, right.SavedRights) &&
               string.Equals(left.OwnerContainerSid, right.OwnerContainerSid, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.PreviousSaclLabel, right.PreviousSaclLabel, StringComparison.Ordinal) &&
               SequenceEqual(left.AllAppliedPaths, right.AllAppliedPaths) &&
               SequenceEqual(left.SourceSids, right.SourceSids);
    }

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }
}

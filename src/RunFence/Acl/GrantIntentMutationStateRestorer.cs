using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public sealed class GrantIntentMutationStateRestorer
{
    private readonly IGrantIntentStoreSaveService grantIntentStoreSaveService;

    public GrantIntentMutationStateRestorer(
        IGrantIntentStoreSaveService grantIntentStoreSaveService)
    {
        this.grantIntentStoreSaveService = grantIntentStoreSaveService;
    }

    public IReadOnlyList<GrantIntentStoreSnapshot> CaptureStoreSnapshots(
        IEnumerable<(IGrantIntentStore Store, string OwnerSid)> storeKeys,
        string normalizedPath,
        string? traversePath,
        bool includeDeny)
    {
        var targetPath = Path.GetFullPath(normalizedPath);
        var normalizedTraversePath = string.IsNullOrEmpty(traversePath) ? null : Path.GetFullPath(traversePath);

        return storeKeys
            .DistinctBy(key => (Store: key.Store, OwnerSid: key.OwnerSid), GrantStoreOwnerKeyComparer.Instance)
            .Select(key => new GrantIntentStoreSnapshot(
                key.Store,
                key.OwnerSid,
                targetPath,
                normalizedTraversePath,
                includeDeny,
                key.Store.GetEntries(key.OwnerSid)
                    .Where(entry => MatchesStoreSnapshot(entry, targetPath, normalizedTraversePath, includeDeny))
                    .Select(entry => entry.Clone())
                    .ToList()))
            .ToList();
    }

    public void TryRestoreStoreSnapshots(
        IReadOnlyList<GrantIntentStoreSnapshot> snapshots,
        string normalizedPath,
        GrantOperationException operationException)
    {
        try
        {
            foreach (var snapshot in snapshots)
            {
                var currentEntries = snapshot.Store.GetEntries(snapshot.OwnerSid)
                    .Where(entry => MatchesStoreSnapshot(
                        entry,
                        snapshot.TargetPath,
                        snapshot.TraversePath,
                        snapshot.IncludeDeny))
                    .ToList();
                foreach (var entry in currentEntries)
                    snapshot.Store.RemoveEntry(snapshot.OwnerSid, entry);

                foreach (var entry in snapshot.Entries)
                    snapshot.Store.AddEntry(snapshot.OwnerSid, entry);
            }

            grantIntentStoreSaveService.Save(
                snapshots.Select(snapshot => snapshot.Store).Distinct(),
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath);
        }
        catch (GrantOperationException ex)
        {
            operationException.AppendCleanupFailure(ex.Step, ex.Path, ex.ConfigPath, ex.Cause);
            operationException.AppendCleanupFailures(ex.CleanupFailures);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.RevertIntentSave,
                normalizedPath,
                grantIntentStoreSaveService.GetPrimaryConfigPath(snapshots.Select(snapshot => snapshot.Store)),
                ex);
        }
    }

    private static bool MatchesStoreSnapshot(
        GrantedPathEntry entry,
        string targetPath,
        string? traversePath,
        bool includeDeny)
    {
        if (entry.IsTraverseOnly)
        {
            return traversePath != null &&
                   string.Equals(entry.Path, traversePath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry.Path, targetPath, StringComparison.OrdinalIgnoreCase) &&
               (includeDeny || !entry.IsDeny);
    }

    private sealed class GrantStoreOwnerKeyComparer : IEqualityComparer<(IGrantIntentStore Store, string OwnerSid)>
    {
        public static GrantStoreOwnerKeyComparer Instance { get; } = new();

        public bool Equals((IGrantIntentStore Store, string OwnerSid) x, (IGrantIntentStore Store, string OwnerSid) y)
            => ReferenceEquals(x.Store, y.Store) &&
               string.Equals(x.OwnerSid, y.OwnerSid, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((IGrantIntentStore Store, string OwnerSid) obj)
            => HashCode.Combine(obj.Store, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.OwnerSid));
    }
}

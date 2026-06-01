using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public class TraverseIntentStoreMutationService(
    ITraverseCoreOperations traverseCore,
    ContainerInteractiveUserSync containerIuSync,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    TraverseGrantStateService traverseGrantStateService,
    IGrantIntentStoreSaveService grantIntentStoreSaveService)
{
    public readonly record struct TraverseMutationPendingSave(
        GrantApplyResult Result,
        IReadOnlyList<IGrantIntentStore> AffectedStores,
        string SavePath);

    public TraverseMutationPendingSave RemoveAllTraverseWithoutSaving(
        string accountSid,
        IReadOnlyList<IGrantIntentStore>? additionalPrimaryStores)
    {
        var traverseLocations = traverseIntentStoreCoordinator.GetAllTraverseLocations(accountSid);
        if (traverseLocations.Count == 0)
            return default;

        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(
            GetCombinedStores(additionalPrimaryStores, traverseLocations.Select(location => location.Store)));
        foreach (var traverseGroup in GroupTraverseLocationsByPath(traverseLocations))
        {
            RemoveTraverseAcesForLocations(
                accountSid,
                traverseGroup.Path,
                traverseGroup.Locations,
                primaryConfigPath,
                GrantApplyFailureStep.RemoveAllTraverseAclRemove);
            RemoveTrackedTraverseWithoutFilesystem(accountSid, traverseGroup.Path);
            RemoveTraverseEntriesFromStores(accountSid, traverseGroup.Path, traverseGroup.Locations);
        }

        return new TraverseMutationPendingSave(
            new GrantApplyResult(TraverseApplied: true, DatabaseModified: true),
            traverseLocations.Select(location => location.Store).Distinct().ToList(),
            traverseLocations[0].Entry.Path);
    }

    public TraverseMutationPendingSave UntrackAllTraverseWithoutSaving(string accountSid)
    {
        var traverseLocations = traverseIntentStoreCoordinator.GetAllTraverseLocations(accountSid);
        if (traverseLocations.Count == 0)
            return default;

        foreach (var traverseGroup in GroupTraverseLocationsByPath(traverseLocations))
        {
            RemoveTrackedTraverseWithoutFilesystem(accountSid, traverseGroup.Path);
            RemoveTraverseEntriesFromStores(accountSid, traverseGroup.Path, traverseGroup.Locations);
        }

        return new TraverseMutationPendingSave(
            new GrantApplyResult(DatabaseModified: true),
            traverseLocations.Select(location => location.Store).Distinct().ToList(),
            traverseLocations[0].Entry.Path);
    }

    public TraverseMutationPendingSave RemoveTraverseLocationsWithoutSaving(
        string accountSid,
        string normalized,
        IReadOnlyList<GrantIntentLocation> existingLocations)
    {
        var primaryConfigPath = grantIntentStoreSaveService.GetPrimaryConfigPath(
            existingLocations.Select(location => location.Store));
        RemoveTraverseAcesForLocations(
            accountSid,
            normalized,
            existingLocations,
            primaryConfigPath,
            GrantApplyFailureStep.TraverseAclRemove);
        RemoveTrackedTraverseWithoutFilesystem(accountSid, normalized);
        RemoveTraverseEntriesFromStores(accountSid, normalized, existingLocations);

        return new TraverseMutationPendingSave(
            new GrantApplyResult(TraverseApplied: true, DatabaseModified: true),
            existingLocations.Select(location => location.Store).Distinct().ToList(),
            normalized);
    }

    public TraverseMutationPendingSave UntrackTraverseLocationsWithoutSaving(
        string accountSid,
        string normalized,
        IReadOnlyList<GrantIntentLocation> existingLocations)
    {
        RemoveTrackedTraverseWithoutFilesystem(accountSid, normalized);
        RemoveTraverseEntriesFromStores(accountSid, normalized, existingLocations);

        return new TraverseMutationPendingSave(
            new GrantApplyResult(DatabaseModified: true),
            existingLocations.Select(location => location.Store).Distinct().ToList(),
            normalized);
    }

    private static IReadOnlyList<IGrantIntentStore> GetCombinedStores(
        IReadOnlyList<IGrantIntentStore>? first,
        IEnumerable<IGrantIntentStore> second)
        => (first ?? [])
            .Concat(second)
            .Distinct()
            .ToList();

    private void RemoveTraverseAcesForLocations(
        string accountSid,
        string normalized,
        IReadOnlyList<GrantIntentLocation> existingLocations,
        string? primaryConfigPath,
        GrantApplyFailureStep failureStep)
    {
        var removingPaths = traverseGrantStateService.CollectStoredTraversePaths(existingLocations.Select(location => location.Entry));
        var remainingEntries = traverseGrantStateService.GetRemainingTraverseEntriesForCleanup(accountSid, existingLocations);
        var grantPaths = traverseGrantStateService.GetTraverseGrantPathsForCleanup(accountSid, []);

        try
        {
            traverseCore.RemoveTraverseAces(
                accountSid,
                removingPaths
                    .Where(pathToRemove =>
                    {
                        if (grantPaths.Contains(pathToRemove))
                            return false;

                        return !remainingEntries.Any(entry => traverseGrantStateService.CollectStoredTraversePaths(entry)
                            .Contains(pathToRemove, StringComparer.OrdinalIgnoreCase));
                    })
                    .ToList());
        }
        catch (Exception ex)
        {
            throw new GrantOperationException(
                failureStep,
                normalized,
                primaryConfigPath,
                ex);
        }
    }

    private static IEnumerable<(string Path, IReadOnlyList<GrantIntentLocation> Locations)>
        GroupTraverseLocationsByPath(IEnumerable<GrantIntentLocation> locations)
        => locations
            .GroupBy(location => location.Entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, (IReadOnlyList<GrantIntentLocation>)group.ToList()));

    private void RemoveTraverseEntriesFromStores(
        string sid,
        string normalizedPath,
        IEnumerable<GrantIntentLocation> locations)
    {
        foreach (var location in locations)
        {
            if (!string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            traverseIntentStoreCoordinator.RemoveTraverseEntryFromStore(sid, location.Store, location.Entry);
        }
    }

    private void RemoveTrackedTraverseWithoutFilesystem(string sid, string normalizedPath)
    {
        bool removed = RemoveTraverseWithoutPersisting(sid, normalizedPath, updateFileSystem: false);

        if (!removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, normalizedPath);
    }

    private bool RemoveTraverseWithoutPersisting(string sid, string path, bool updateFileSystem)
    {
        bool removed = traverseCore.RemoveTraverse(sid, path, updateFileSystem);

        if (removed && AclHelper.IsContainerSid(sid))
            containerIuSync.RevertInteractiveUserTraverse(sid, Path.GetFullPath(path));

        return removed;
    }
}

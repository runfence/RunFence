using RunFence.Core.Models;

namespace RunFence.Persistence;

public class GrantIntentRepository(IGrantIntentStoreProvider grantIntentStoreProvider)
    : IGrantIntentRepository
{
    public GrantIntentLocation? FindGrant(string sid, GrantedPathEntry entry)
        => FindLocations(sid, entry, traverseOnly: false).FirstOrDefault();

    public IReadOnlyList<GrantIntentLocation> FindGrantLocations(string sid, GrantedPathEntry entry)
        => FindLocations(sid, entry, traverseOnly: false);

    public GrantIntentLocation? FindTraverse(string sid, GrantedPathEntry entry)
        => FindLocations(sid, entry, traverseOnly: true).FirstOrDefault();

    public IReadOnlyList<GrantIntentLocation> FindTraverseLocations(string sid, GrantedPathEntry entry)
        => FindLocations(sid, entry, traverseOnly: true);

    public IReadOnlyList<GrantIntentLocation> FindEntriesForSid(string sid)
        => grantIntentStoreProvider.GetLoadedStores()
            .SelectMany(store => store.GetEntries(sid)
                .Select(entry => new GrantIntentLocation(entry, store)))
            .ToList();

    private IReadOnlyList<GrantIntentLocation> FindLocations(
        string sid,
        GrantedPathEntry entry,
        bool traverseOnly)
    {
        var targetIdentity = GrantIntentEntryIdentity.From(sid, entry);
        return grantIntentStoreProvider.GetLoadedStores()
            .SelectMany(store => store.GetEntries(sid)
                .Where(candidate =>
                    candidate.IsTraverseOnly == traverseOnly &&
                    GrantIntentEntryIdentity.From(sid, candidate) == targetIdentity)
                .Select(candidate => new GrantIntentLocation(candidate, store)))
            .ToList();
    }

}

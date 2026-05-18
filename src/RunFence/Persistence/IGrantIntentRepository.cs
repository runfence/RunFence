using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IGrantIntentRepository
{
    GrantIntentLocation? FindGrant(string sid, GrantedPathEntry entry);

    IReadOnlyList<GrantIntentLocation> FindGrantLocations(string sid, GrantedPathEntry entry);

    GrantIntentLocation? FindTraverse(string sid, GrantedPathEntry entry);

    IReadOnlyList<GrantIntentLocation> FindTraverseLocations(string sid, GrantedPathEntry entry);

    IReadOnlyList<GrantIntentLocation> FindEntriesForSid(string sid);
}

using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockLocalGroupQueryService(ILocalGroupQueryService real, NonElevatedMockStore store)
    : ILocalGroupQueryService
{
    public List<LocalUserAccount> GetLocalGroups()
        => [.. real.GetLocalGroups(), .. store.GetAllFakeGroups()];

    public List<LocalUserAccount> GetGroupsForUser(string sid)
    {
        var result = real.GetGroupsForUser(sid);
        var knownSids = new HashSet<string>(result.Select(g => g.Sid), StringComparer.OrdinalIgnoreCase);
        List<LocalUserAccount>? realGroups = null;
        foreach (var groupSid in store.GetStoredGroupSidsForUser(sid))
        {
            if (!knownSids.Add(groupSid))
                continue;

            var group = store.IsFakeGroup(groupSid)
                ? store.GetFakeGroup(groupSid)
                : (realGroups ??= real.GetLocalGroups()).FirstOrDefault(g =>
                    string.Equals(g.Sid, groupSid, StringComparison.OrdinalIgnoreCase));
            if (group != null)
                result.Add(group);
        }

        return result;
    }

    public List<LocalUserAccount> GetMembersOfGroup(string groupSid)
        => store.IsFakeGroup(groupSid)
            ? store.GetMembersOfGroup(groupSid)
            : [.. real.GetMembersOfGroup(groupSid), .. store.GetMembersOfGroup(groupSid)];

    public bool IsLocalGroup(string sid)
        => real.IsLocalGroup(sid) || store.IsFakeGroup(sid);

    public string? GetGroupDescription(string groupSid)
        => store.IsFakeGroup(groupSid)
            ? store.GetGroupDescription(groupSid)
            : real.GetGroupDescription(groupSid);

    public bool IsUserAccountEnabled(string username)
        => store.IsFakeUsername(username) || real.IsUserAccountEnabled(username);

    public GroupQueryResult QueryGroupsForUser(string sid)
    {
        var groups = GetGroupsForUser(sid);
        return new GroupQueryResult(GroupQueryStatus.Succeeded, null, null, [.. groups.Select(g => g.Sid)], groups, null, null);
    }

    public GroupQueryResult QueryLocalGroups()
    {
        var groups = GetLocalGroups();
        return new GroupQueryResult(GroupQueryStatus.Succeeded, null, null, [.. groups.Select(g => g.Sid)], groups, null, null);
    }

    public GroupQueryResult QueryMembersOfGroup(string groupSid)
    {
        var members = GetMembersOfGroup(groupSid);
        return new GroupQueryResult(GroupQueryStatus.Succeeded, groupSid, null, [.. members.Select(g => g.Sid)], members, null, null);
    }

    public GroupQueryResult QueryGroupDescription(string groupSid)
    {
        var description = GetGroupDescription(groupSid);
        if (description == null && !IsLocalGroup(groupSid))
            return new GroupQueryResult(GroupQueryStatus.NotFound, groupSid, null, null, null, null, null);
        return new GroupQueryResult(GroupQueryStatus.Succeeded, groupSid, null, null, null, description, null);
    }
}

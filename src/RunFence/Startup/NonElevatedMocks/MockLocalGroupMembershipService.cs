using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockLocalGroupMembershipService(ILocalGroupMembershipService real, NonElevatedMockStore store) : ILocalGroupMembershipService
{
    // Read operations combine real system data with in-memory mock state.
    // Write operations update in-memory state only (no Windows API calls).

    public List<LocalUserAccount> GetLocalGroups()
        => [..real.GetLocalGroups(), ..store.GetAllFakeGroups()];

    public List<LocalUserAccount> GetGroupsForUser(string sid)
    {
        var result = real.GetGroupsForUser(sid);
        var knownSids = new HashSet<string>(result.Select(g => g.Sid), StringComparer.OrdinalIgnoreCase);
        List<LocalUserAccount>? realGroups = null;
        foreach (var groupSid in store.GetStoredGroupSidsForUser(sid))
        {
            if (!knownSids.Add(groupSid)) continue;
            var group = store.IsFakeGroup(groupSid)
                ? store.GetFakeGroup(groupSid)
                : (realGroups ??= real.GetLocalGroups()).FirstOrDefault(g => string.Equals(g.Sid, groupSid, StringComparison.OrdinalIgnoreCase));
            if (group != null) result.Add(group);
        }
        return result;
    }

    public List<LocalUserAccount> GetMembersOfGroup(string groupSid)
        => store.IsFakeGroup(groupSid)
            ? store.GetMembersOfGroup(groupSid)
            : [..real.GetMembersOfGroup(groupSid), ..store.GetMembersOfGroup(groupSid)];

    public bool IsLocalGroup(string sid)
        => real.IsLocalGroup(sid) || store.IsFakeGroup(sid);

    public string? GetGroupDescription(string groupSid)
        => store.IsFakeGroup(groupSid)
            ? store.GetGroupDescription(groupSid)
            : real.GetGroupDescription(groupSid);

    public bool IsUserAccountEnabled(string username)
        => store.IsFakeUsername(username) || real.IsUserAccountEnabled(username);

    public void AddUserToGroups(string userSid, string username, List<string> groupSids)
        => store.AddMemberships(userSid, username, groupSids);

    public void RemoveUserFromGroups(string userSid, string username, List<string> groupSids)
        => store.RemoveMemberships(userSid, groupSids);

    public string CreateGroup(string groupName, string? description)
    {
        var sid = store.DeriveFakeSid(groupName, ridBase: 30001);
        store.AddGroup(sid, groupName, description);
        return sid;
    }

    public void DeleteGroup(string groupSid) => store.RemoveGroup(groupSid);

    public void UpdateGroupDescription(string groupSid, string description)
        => store.UpdateGroupDescription(groupSid, description);
}

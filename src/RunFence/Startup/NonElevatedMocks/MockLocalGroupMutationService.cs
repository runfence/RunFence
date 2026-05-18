using RunFence.Infrastructure;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockLocalGroupMutationService : ILocalGroupMutationService
{
    private readonly NonElevatedMockStore _store;

    public MockLocalGroupMutationService(ILocalGroupMutationService localGroupMutationService, NonElevatedMockStore store)
    {
        ArgumentNullException.ThrowIfNull(localGroupMutationService);
        _store = store;
    }

    public void AddUserToGroups(string userSid, string username, List<string> groupSids)
        => _store.AddMemberships(userSid, username, groupSids);

    public GroupMutationResult AddUserToGroupsWithResult(string userSid, string username, List<string> groupSids)
    {
        AddUserToGroups(userSid, username, groupSids);
        return new GroupMutationResult(GroupMutationStatus.Succeeded, null, null, groupSids, null);
    }

    public void RemoveUserFromGroups(string userSid, string username, List<string> groupSids)
        => _store.RemoveMemberships(userSid, groupSids);

    public GroupMutationResult RemoveUserFromGroupsWithResult(string userSid, string username, List<string> groupSids)
    {
        RemoveUserFromGroups(userSid, username, groupSids);
        return new GroupMutationResult(GroupMutationStatus.Succeeded, null, null, groupSids, null);
    }

    public string CreateGroup(string groupName, string? description)
    {
        var sid = _store.DeriveFakeSid(groupName, ridBase: 30001);
        _store.AddGroup(sid, groupName, description);
        return sid;
    }

    public GroupMutationResult CreateGroupWithResult(string groupName, string? description)
    {
        var sid = CreateGroup(groupName, description);
        return new GroupMutationResult(GroupMutationStatus.Succeeded, sid, groupName, null, null);
    }

    public void DeleteGroup(string groupSid)
        => _store.RemoveGroup(groupSid);

    public GroupMutationResult DeleteGroupWithResult(string groupSid)
    {
        try
        {
            DeleteGroup(groupSid);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, groupSid, null, null, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, groupSid, null, null, ex.Message);
        }
    }

    public void UpdateGroupDescription(string groupSid, string description)
        => _store.UpdateGroupDescription(groupSid, description);

    public GroupMutationResult UpdateGroupDescriptionWithResult(string groupSid, string description)
    {
        try
        {
            UpdateGroupDescription(groupSid, description);
            return new GroupMutationResult(GroupMutationStatus.Succeeded, groupSid, null, null, null);
        }
        catch (Exception ex)
        {
            return new GroupMutationResult(GroupMutationStatus.Failed, groupSid, null, null, ex.Message);
        }
    }
}

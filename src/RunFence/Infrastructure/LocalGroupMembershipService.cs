using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public class LocalGroupMembershipService(
    ILocalGroupQueryService queryService,
    ILocalGroupMutationService mutationService) : ILocalGroupMembershipService
{
    public GroupQueryResult QueryGroupsForUser(string sid) => queryService.QueryGroupsForUser(sid);
    public GroupQueryResult QueryLocalGroups() => queryService.QueryLocalGroups();
    public GroupQueryResult QueryMembersOfGroup(string groupSid) => queryService.QueryMembersOfGroup(groupSid);
    public GroupQueryResult QueryGroupDescription(string groupSid) => queryService.QueryGroupDescription(groupSid);
    public List<LocalUserAccount> GetGroupsForUser(string sid) => queryService.GetGroupsForUser(sid);
    public List<LocalUserAccount> GetLocalGroups() => queryService.GetLocalGroups();
    public bool IsLocalGroup(string sid) => queryService.IsLocalGroup(sid);
    public List<LocalUserAccount> GetMembersOfGroup(string groupSid) => queryService.GetMembersOfGroup(groupSid);
    public string? GetGroupDescription(string groupSid) => queryService.GetGroupDescription(groupSid);
    public bool IsUserAccountEnabled(string username) => queryService.IsUserAccountEnabled(username);

    public GroupMutationResult AddUserToGroupsWithResult(string userSid, string username, List<string> groupSids)
        => mutationService.AddUserToGroupsWithResult(userSid, username, groupSids);

    public GroupMutationResult RemoveUserFromGroupsWithResult(string userSid, string username, List<string> groupSids)
        => mutationService.RemoveUserFromGroupsWithResult(userSid, username, groupSids);

    public GroupMutationResult CreateGroupWithResult(string groupName, string? description)
        => mutationService.CreateGroupWithResult(groupName, description);

    public GroupMutationResult DeleteGroupWithResult(string groupSid)
        => mutationService.DeleteGroupWithResult(groupSid);

    public GroupMutationResult UpdateGroupDescriptionWithResult(string groupSid, string description)
        => mutationService.UpdateGroupDescriptionWithResult(groupSid, description);

    public void AddUserToGroups(string userSid, string username, List<string> groupSids)
        => mutationService.AddUserToGroups(userSid, username, groupSids);

    public void RemoveUserFromGroups(string userSid, string username, List<string> groupSids)
        => mutationService.RemoveUserFromGroups(userSid, username, groupSids);

    public string CreateGroup(string groupName, string? description)
        => mutationService.CreateGroup(groupName, description);

    public void DeleteGroup(string groupSid)
        => mutationService.DeleteGroup(groupSid);

    public void UpdateGroupDescription(string groupSid, string description)
        => mutationService.UpdateGroupDescription(groupSid, description);
}

namespace RunFence.Infrastructure;

public interface ILocalGroupMutationService
{
    GroupMutationResult AddUserToGroupsWithResult(string userSid, string username, List<string> groupSids);
    GroupMutationResult RemoveUserFromGroupsWithResult(string userSid, string username, List<string> groupSids);
    GroupMutationResult CreateGroupWithResult(string groupName, string? description);
    GroupMutationResult DeleteGroupWithResult(string groupSid);
    GroupMutationResult UpdateGroupDescriptionWithResult(string groupSid, string description);
    void AddUserToGroups(string userSid, string username, List<string> groupSids);
    void RemoveUserFromGroups(string userSid, string username, List<string> groupSids);
    string CreateGroup(string groupName, string? description);
    void DeleteGroup(string groupSid);
    void UpdateGroupDescription(string groupSid, string description);
}

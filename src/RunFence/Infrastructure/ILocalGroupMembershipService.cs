using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface ILocalGroupMembershipService
{
    void AddUserToGroups(string userSid, string username, List<string> groupSids);
    void RemoveUserFromGroups(string userSid, string username, List<string> groupSids);
    List<LocalUserAccount> GetGroupsForUser(string sid);
    List<LocalUserAccount> GetLocalGroups();
    bool IsLocalGroup(string sid);
    string CreateGroup(string groupName, string? description);
    void DeleteGroup(string groupSid);
    void UpdateGroupDescription(string groupSid, string description);
    List<LocalUserAccount> GetMembersOfGroup(string groupSid);
    string? GetGroupDescription(string groupSid);

    /// <summary>
    /// Returns true if the local user account with the given SAM account name exists and is enabled.
    /// Returns false for domain accounts, groups, or accounts that cannot be resolved locally.
    /// </summary>
    bool IsUserAccountEnabled(string username);
}
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface ILocalGroupQueryService
{
    GroupQueryResult QueryGroupsForUser(string sid);
    GroupQueryResult QueryLocalGroups();
    GroupQueryResult QueryMembersOfGroup(string groupSid);
    GroupQueryResult QueryGroupDescription(string groupSid);
    List<LocalUserAccount> GetGroupsForUser(string sid);
    List<LocalUserAccount> GetLocalGroups();
    bool IsLocalGroup(string sid);
    List<LocalUserAccount> GetMembersOfGroup(string groupSid);
    string? GetGroupDescription(string groupSid);
    bool IsUserAccountEnabled(string username);
}

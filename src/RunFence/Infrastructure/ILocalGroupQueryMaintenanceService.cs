namespace RunFence.Infrastructure;

public interface ILocalGroupQueryMaintenanceService
{
    void InvalidateUserGroupMembership(string userSid, IEnumerable<string> groupSids);
    void InvalidateLocalGroups();
    void InvalidateGroupDetails(string groupSid);
    string? ResolveGroupName(string groupSid);
}

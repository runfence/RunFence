using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public class LocalGroupQueryService(
    ILoggingService log,
    GroupMembershipApi groupMembership,
    ISidResolver sidResolver,
    ILocalUserProvider localUserProvider) : ILocalGroupQueryService, ILocalGroupQueryMaintenanceService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly CachedLookup<bool, List<LocalUserAccount>> _localGroupsCache = new(CacheTtl);
    private readonly CachedLookup<string, List<LocalUserAccount>> _groupsForUserCache =
        new(CacheTtl, StringComparer.OrdinalIgnoreCase);
    private readonly CachedLookup<string, List<LocalUserAccount>> _membersCache =
        new(CacheTtl, StringComparer.OrdinalIgnoreCase);
    private readonly CachedLookup<string, string?> _descriptionCache =
        new(CacheTtl, StringComparer.OrdinalIgnoreCase);

    public GroupQueryResult QueryGroupsForUser(string sid)
    {
        try
        {
            var groups = GetGroupsForUser(sid);
            return new(GroupQueryStatus.Succeeded, null, null, groups.Select(g => g.Sid).ToList(), groups, null, null);
        }
        catch (Exception ex)
        {
            return new(GroupQueryStatus.Failed, null, null, null, null, null, ex.Message);
        }
    }

    public List<LocalUserAccount> GetGroupsForUser(string sid)
        => _groupsForUserCache.Get(sid, () => FetchGroupsForUser(sid));

    private List<LocalUserAccount> FetchGroupsForUser(string sid)
    {
        try
        {
            if (IsLocalGroup(sid))
                return [];

            var name = localUserProvider.GetLocalUserAccounts()
                           .FirstOrDefault(u => string.Equals(u.Sid, sid, StringComparison.OrdinalIgnoreCase))
                           ?.Username
                       ?? sidResolver.TryResolveName(sid);
            if (name == null)
                return [];

            var netApiResult = groupMembership.NetUserGetLocalGroups(name);
            if (netApiResult.ReturnCode != 0 || netApiResult.BufPtr == IntPtr.Zero)
            {
                if (netApiResult.BufPtr != IntPtr.Zero)
                    GroupMembershipNative.NetApiBufferFree(netApiResult.BufPtr);
                return [];
            }

            try
            {
                var groups = new List<LocalUserAccount>();
                var structSize = Marshal.SizeOf<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>();
                for (var i = 0; i < netApiResult.EntriesRead; i++)
                {
                    var entry = Marshal.PtrToStructure<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>(
                        IntPtr.Add(netApiResult.BufPtr, i * structSize));
                    var groupSid = sidResolver.TryResolveSid(entry.lgrui0_name);
                    if (groupSid != null)
                        groups.Add(new LocalUserAccount(entry.lgrui0_name, groupSid));
                }

                return groups;
            }
            finally
            {
                GroupMembershipNative.NetApiBufferFree(netApiResult.BufPtr);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to get groups for user SID {sid}", ex);
            return [];
        }
    }

    public GroupQueryResult QueryLocalGroups()
    {
        try
        {
            var groups = GetLocalGroups();
            return new(GroupQueryStatus.Succeeded, null, null, groups.Select(g => g.Sid).ToList(), groups, null, null);
        }
        catch (Exception ex)
        {
            return new(GroupQueryStatus.Failed, null, null, null, null, null, ex.Message);
        }
    }

    public List<LocalUserAccount> GetLocalGroups()
        => _localGroupsCache.Get(true, FetchLocalGroups);

    private List<LocalUserAccount> FetchLocalGroups()
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var queryFilter = new GroupPrincipal(context);
            using var searcher = new PrincipalSearcher(queryFilter);
            var groups = new List<LocalUserAccount>();
            var allPrincipals = groupMembership.GetLocalGroups(() => searcher.FindAll().ToList());
            foreach (var principal in allPrincipals)
            {
                try
                {
                    if (principal.Sid != null && principal.SamAccountName != null)
                        groups.Add(new LocalUserAccount(principal.SamAccountName, principal.Sid.Value));
                }
                finally
                {
                    principal.Dispose();
                }
            }

            return groups.OrderBy(g => g.Username, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            log.Error("Failed to enumerate local groups", ex);
            return [];
        }
    }

    public GroupQueryResult QueryMembersOfGroup(string groupSid)
    {
        try
        {
            var members = GetMembersOfGroup(groupSid);
            return new(GroupQueryStatus.Succeeded, groupSid, null, members.Select(m => m.Sid).ToList(), members, null, null);
        }
        catch (Exception ex)
        {
            return new(GroupQueryStatus.Failed, groupSid, null, null, null, null, ex.Message);
        }
    }

    public List<LocalUserAccount> GetMembersOfGroup(string groupSid)
        => _membersCache.Get(groupSid, () => FetchMembersOfGroup(groupSid));

    private List<LocalUserAccount> FetchMembersOfGroup(string groupSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
            if (group == null)
                return [];

            var members = new List<LocalUserAccount>();
            var allPrincipals = groupMembership.GetMembersOfGroup(groupSid, () => group.Members.ToList());
            foreach (var principal in allPrincipals)
            {
                try
                {
                    if (principal.Sid != null && principal.SamAccountName != null)
                        members.Add(new LocalUserAccount(principal.SamAccountName, principal.Sid.Value));
                }
                finally
                {
                    principal.Dispose();
                }
            }

            return members.OrderBy(m => m.Username, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            log.Error($"Failed to get members of group SID {groupSid}", ex);
            return [];
        }
    }

    public GroupQueryResult QueryGroupDescription(string groupSid)
    {
        try
        {
            var description = GetGroupDescription(groupSid);
            return description == null
                ? new(GroupQueryStatus.NotFound, groupSid, null, null, null, null, null)
                : new(GroupQueryStatus.Succeeded, groupSid, null, null, null, description, null);
        }
        catch (Exception ex)
        {
            return new(GroupQueryStatus.Failed, groupSid, null, null, null, null, ex.Message);
        }
    }

    public string? GetGroupDescription(string groupSid)
        => _descriptionCache.Get(groupSid, () => FetchGroupDescription(groupSid));

    private string? FetchGroupDescription(string groupSid)
    {
        try
        {
            var groupName = GetGroupName(groupSid);
            return groupName == null ? null : groupMembership.ReadLocalGroupDescription(groupName, groupSid);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to get description for group SID {groupSid}", ex);
            return null;
        }
    }

    public bool IsLocalGroup(string sid) =>
        sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase) ||
        GetLocalGroups().Any(g => string.Equals(g.Sid, sid, StringComparison.OrdinalIgnoreCase));

    public bool IsUserAccountEnabled(string username) => groupMembership.IsUserAccountEnabled(username);

    void ILocalGroupQueryMaintenanceService.InvalidateUserGroupMembership(string userSid, IEnumerable<string> groupSids)
    {
        _groupsForUserCache.Invalidate(userSid);
        _membersCache.InvalidateAll(groupSids);
    }

    void ILocalGroupQueryMaintenanceService.InvalidateLocalGroups()
    {
        _localGroupsCache.Clear();
    }

    void ILocalGroupQueryMaintenanceService.InvalidateGroupDetails(string groupSid)
    {
        _membersCache.Invalidate(groupSid);
        _descriptionCache.Invalidate(groupSid);
    }

    string? ILocalGroupQueryMaintenanceService.ResolveGroupName(string groupSid)
        => ResolveGroupNameCore(groupSid);

    private string? ResolveGroupNameCore(string groupSid)
    {
        var cached = GetLocalGroups().FirstOrDefault(g =>
            string.Equals(g.Sid, groupSid, StringComparison.OrdinalIgnoreCase));
        if (cached != null)
            return cached.Username;

        var resolved = sidResolver.TryResolveName(groupSid);
        if (resolved == null)
            return null;

        var backslash = resolved.IndexOf('\\');
        return backslash >= 0 ? resolved[(backslash + 1)..] : resolved;
    }

    private string? GetGroupName(string groupSid) => ResolveGroupNameCore(groupSid);
}

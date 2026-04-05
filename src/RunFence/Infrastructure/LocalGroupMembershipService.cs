using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public class LocalGroupMembershipService(
    ILoggingService log,
    GroupMembershipApi groupMembership,
    ISidResolver sidResolver,
    ILocalUserProvider localUserProvider) : ILocalGroupMembershipService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly Lock _cacheLock = new();

    private List<LocalUserAccount>? _localGroupsCache;
    private DateTime _localGroupsCacheTime;

    private readonly Dictionary<string, (List<LocalUserAccount> Data, DateTime Time)> _groupsForUserCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (List<LocalUserAccount> Data, DateTime Time)> _membersCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string? Data, DateTime Time)> _descriptionCache
        = new(StringComparer.OrdinalIgnoreCase);

    public void AddUserToGroups(string userSid, string username, List<string> groupSids)
    {
        try { ModifyGroupMembership(userSid, username, groupSids, (group, user) => group.Members.Add(user), "add"); }
        finally { lock (_cacheLock) { _groupsForUserCache.Remove(userSid); foreach (var g in groupSids) _membersCache.Remove(g); } }
    }

    public void RemoveUserFromGroups(string userSid, string username, List<string> groupSids)
    {
        try { ModifyGroupMembership(userSid, username, groupSids, (group, user) => group.Members.Remove(user), "remove"); }
        finally { lock (_cacheLock) { _groupsForUserCache.Remove(userSid); foreach (var g in groupSids) _membersCache.Remove(g); } }
    }

    private void ModifyGroupMembership(string userSid, string username, IEnumerable<string> groupSids,
        Action<GroupPrincipal, UserPrincipal> operation, string operationVerb)
    {
        using var context = new PrincipalContext(ContextType.Machine);
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, userSid);
        if (user == null)
            throw new InvalidOperationException($"Account not found for SID {userSid}.");

        var pastVerb = operationVerb == "add" ? "Added" : "Removed";
        var preposition = operationVerb == "add" ? "to" : "from";
        var errors = new List<string>();
        foreach (var groupSid in groupSids)
        {
            try
            {
                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
                if (group == null)
                {
                    log.Error($"Group not found for SID: {groupSid}");
                    errors.Add($"Group with SID '{groupSid}' not found");
                    continue;
                }

                var groupName = group.SamAccountName ?? groupSid;
                operation(group, user);
                group.Save();
                log.Info($"{pastVerb} {username} {preposition} group {groupName}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to {operationVerb} {username} {preposition} group SID {groupSid}", ex);
                errors.Add($"Failed to {operationVerb} {preposition} group '{groupSid}': {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));
    }

    public List<LocalUserAccount> GetGroupsForUser(string sid)
    {
        lock (_cacheLock)
        {
            if (_groupsForUserCache.TryGetValue(sid, out var cached) && DateTime.UtcNow - cached.Time < CacheTtl)
                return cached.Data;
        }
        var result = FetchGroupsForUser(sid);
        lock (_cacheLock)
        {
            if (!_groupsForUserCache.TryGetValue(sid, out var existing) || DateTime.UtcNow - existing.Time >= CacheTtl)
                _groupsForUserCache[sid] = (result, DateTime.UtcNow);
        }
        return result;
    }

    private List<LocalUserAccount> FetchGroupsForUser(string sid)
    {
        try
        {
            if (IsLocalGroup(sid))
                return [];

            var name = localUserProvider.GetLocalUserAccounts()
                .FirstOrDefault(u => string.Equals(u.Sid, sid, StringComparison.OrdinalIgnoreCase))?.Username;
            if (name == null)
                name = sidResolver.TryResolveName(sid);
            if (name == null)
                return [];

            var netApiResult = groupMembership.NetUserGetLocalGroups(name, () => CallNetUserGetLocalGroups(name));

            if (netApiResult.ReturnCode != 0)
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

    private static NetUserGroupsResult CallNetUserGetLocalGroups(string username)
    {
        var ret = GroupMembershipNative.NetUserGetLocalGroups(null, username, 0, GroupMembershipNative.LgIncludeIndirect,
            out var bufPtr, -1, out var entriesRead, out _);
        return new NetUserGroupsResult(ret, bufPtr, entriesRead);
    }

    public List<LocalUserAccount> GetLocalGroups()
    {
        lock (_cacheLock)
        {
            if (_localGroupsCache != null && DateTime.UtcNow - _localGroupsCacheTime < CacheTtl)
                return _localGroupsCache;
        }
        var result = FetchLocalGroups();
        lock (_cacheLock)
        {
            if (_localGroupsCache == null || DateTime.UtcNow - _localGroupsCacheTime >= CacheTtl)
            {
                _localGroupsCache = result;
                _localGroupsCacheTime = DateTime.UtcNow;
            }
        }
        return result;
    }

    private List<LocalUserAccount> FetchLocalGroups()
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var queryFilter = new GroupPrincipal(context);
            using var searcher = new PrincipalSearcher(queryFilter);
            var groups = new List<LocalUserAccount>();
            var allPrincipals = groupMembership.GetLocalGroups(
                () => searcher.FindAll().Cast<Principal>().ToList());
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
            return new List<LocalUserAccount>();
        }
    }

    public string CreateGroup(string groupName, string? description)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = new GroupPrincipal(context);
            group.Name = groupName;
            if (!string.IsNullOrEmpty(description))
                group.Description = description;
            group.Save();
            var sid = group.Sid.Value;
            log.Info($"Created local group: {groupName} ({sid})");
            return sid;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create local group {groupName}", ex);
            throw new InvalidOperationException($"Failed to create group: {ex.Message}", ex);
        }
        finally
        {
            lock (_cacheLock) { _localGroupsCache = null; }
        }
    }

    public void DeleteGroup(string groupSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
            if (group != null)
            {
                var name = group.SamAccountName;
                group.Delete();
                log.Info($"Deleted local group: {name} ({groupSid})");
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to delete group SID {groupSid}", ex);
            throw new InvalidOperationException($"Failed to delete group: {ex.Message}", ex);
        }
        finally
        {
            lock (_cacheLock) { _localGroupsCache = null; _membersCache.Remove(groupSid); _descriptionCache.Remove(groupSid); }
        }
    }

    public void UpdateGroupDescription(string groupSid, string description)
    {
        try
        {
            var groupName = GetGroupName(groupSid)
                ?? throw new InvalidOperationException($"Group not found for SID {groupSid}.");
            var info = new GroupMembershipNative.LOCALGROUP_INFO_1002 { lgrpi1002_comment = description };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<GroupMembershipNative.LOCALGROUP_INFO_1002>());
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var ret = GroupMembershipNative.NetLocalGroupSetInfo(null, groupName, 1002, ptr, out _);
                if (ret != 0)
                    throw new InvalidOperationException($"NetLocalGroupSetInfo failed with error code {ret}.");
                log.Info($"Updated description for group {groupName} ({groupSid})");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to update group description for SID {groupSid}", ex);
            throw new InvalidOperationException($"Failed to update group: {ex.Message}", ex);
        }
        finally
        {
            lock (_cacheLock) { _descriptionCache.Remove(groupSid); }
        }
    }

    public List<LocalUserAccount> GetMembersOfGroup(string groupSid)
    {
        lock (_cacheLock)
        {
            if (_membersCache.TryGetValue(groupSid, out var cached) && DateTime.UtcNow - cached.Time < CacheTtl)
                return cached.Data;
        }
        var result = FetchMembersOfGroup(groupSid);
        lock (_cacheLock)
        {
            if (!_membersCache.TryGetValue(groupSid, out var existing) || DateTime.UtcNow - existing.Time >= CacheTtl)
                _membersCache[groupSid] = (result, DateTime.UtcNow);
        }
        return result;
    }

    private List<LocalUserAccount> FetchMembersOfGroup(string groupSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
            if (group == null)
                return [];

            var members = new List<LocalUserAccount>();
            var allPrincipals = groupMembership.GetMembersOfGroup(groupSid,
                () => group.Members.Cast<Principal>().ToList());
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

    public string? GetGroupDescription(string groupSid)
    {
        lock (_cacheLock)
        {
            if (_descriptionCache.TryGetValue(groupSid, out var cached) && DateTime.UtcNow - cached.Time < CacheTtl)
                return cached.Data;
        }
        var result = FetchGroupDescription(groupSid);
        lock (_cacheLock)
        {
            if (!_descriptionCache.TryGetValue(groupSid, out var existing) || DateTime.UtcNow - existing.Time >= CacheTtl)
                _descriptionCache[groupSid] = (result, DateTime.UtcNow);
        }
        return result;
    }

    private string? FetchGroupDescription(string groupSid)
    {
        try
        {
            var groupName = GetGroupName(groupSid);
            if (groupName == null) return null;

            return groupMembership.GetGroupDescription(groupSid, () =>
            {
                var ret = GroupMembershipNative.NetLocalGroupGetInfo(null, groupName, 1, out var bufPtr);
                if (ret != 0) return null;
                try
                {
                    return Marshal.PtrToStructure<GroupMembershipNative.LOCALGROUP_INFO_1>(bufPtr).lgrpi1_comment;
                }
                finally
                {
                    GroupMembershipNative.NetApiBufferFree(bufPtr);
                }
            });
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

    private string? GetGroupName(string groupSid)
    {
        var cached = GetLocalGroups().FirstOrDefault(g =>
            string.Equals(g.Sid, groupSid, StringComparison.OrdinalIgnoreCase));
        if (cached != null) return cached.Username;

        var resolved = sidResolver.TryResolveName(groupSid);
        if (resolved == null) return null;
        var backslash = resolved.IndexOf('\\');
        return backslash >= 0 ? resolved[(backslash + 1)..] : resolved;
    }
}

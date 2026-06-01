using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

public readonly record struct NetUserGroupsResult(int ReturnCode, IntPtr BufPtr, int EntriesRead);

public class GroupMembershipApi(ILoggingService log)
{
    private const int WarnThresholdMs = 100;

    /// <summary>Used by LocalGroupQueryService.GetLocalGroups — wraps PrincipalSearcher.FindAll.</summary>
    public List<Principal> GetLocalGroups(Func<List<Principal>> call)
    {
        List<Principal>? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = call();
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): PrincipalSearcher.FindAll → {result?.Count.ToString() ?? "failed"}");
        }
    }

    /// <summary>Used by LocalGroupQueryService.GetMembersOfGroup — wraps GroupPrincipal.Members.</summary>
    public List<Principal> GetMembersOfGroup(string groupSid, Func<List<Principal>> call)
    {
        List<Principal>? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = call();
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): GroupPrincipal.Members({groupSid}) → {result?.Count.ToString() ?? "failed"}");
        }
    }

    /// <summary>Used by LocalGroupQueryService.FetchGroupsForUser and AclPermissionService.TryResolveLocalGroupSids — wraps NetUserGetLocalGroups P/Invoke.</summary>
    public NetUserGroupsResult NetUserGetLocalGroups(string username, Func<NetUserGroupsResult> call)
    {
        var result = default(NetUserGroupsResult);
        var sw = Stopwatch.StartNew();
        try
        {
            result = call();
            return result;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
            {
                var detail = result.ReturnCode == 0 ? $"{result.EntriesRead} entries" : $"error {result.ReturnCode}";
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NetUserGetLocalGroups({username}) → {detail}");
            }
        }
    }

    public NetUserGroupsResult NetUserGetLocalGroups(string username)
    {
        return NetUserGetLocalGroups(username, () =>
        {
            var ret = GroupMembershipNative.NetUserGetLocalGroups(
                null,
                username,
                0,
                GroupMembershipNative.LgIncludeIndirect,
                out var bufPtr,
                -1,
                out var entriesRead,
                out _);
            return new NetUserGroupsResult(ret, bufPtr, entriesRead);
        });
    }

    /// <summary>Used by LocalGroupQueryService.GetGroupDescription — wraps GroupPrincipal.Description read.</summary>
    public string? GetGroupDescription(string groupSid, Func<string?> call)
    {
        string? result = null;
        var sw = Stopwatch.StartNew();
        try { return result = call(); }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): GroupPrincipal.Description({groupSid}) → {(result != null ? "found" : "null")}");
        }
    }

    public string? ReadLocalGroupDescription(string groupName, string groupSidForLog)
    {
        return GetGroupDescription(groupSidForLog, () =>
        {
            var ret = GroupMembershipNative.NetLocalGroupGetInfo(null, groupName, 1, out var bufPtr);
            if (ret != 0 || bufPtr == IntPtr.Zero)
                return null;

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

    public void UpdateLocalGroupDescription(string groupName, string? description, string groupSidForLog)
    {
        var sw = Stopwatch.StartNew();
        var success = false;
        try
        {
            var info = new GroupMembershipNative.LOCALGROUP_INFO_1002
            {
                lgrpi1002_comment = description
            };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<GroupMembershipNative.LOCALGROUP_INFO_1002>());
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var ret = GroupMembershipNative.NetLocalGroupSetInfo(null, groupName, 1002, ptr, out _);
                if (ret != 0)
                    throw new InvalidOperationException($"NetLocalGroupSetInfo failed with error code {ret}.");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            success = true;
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NetLocalGroupSetInfo({groupSidForLog}) → {(success ? "ok" : "failed")}");
        }
    }

    public bool IsUserAccountEnabled(string username)
    {
        var sw = Stopwatch.StartNew();
        var enabled = false;
        var success = false;
        var retCode = 0;
        try
        {
            retCode = GroupMembershipNative.NetUserGetInfo(null, username, 1, out var bufPtr);
            if (retCode != 0 || bufPtr == IntPtr.Zero)
                return false;

            try
            {
                var info = Marshal.PtrToStructure<GroupMembershipNative.USER_INFO_1>(bufPtr);
                enabled = (info.usri1_flags & GroupMembershipNative.UF_ACCOUNTDISABLE) == 0;
                success = true;
                return enabled;
            }
            finally
            {
                GroupMembershipNative.NetApiBufferFree(bufPtr);
            }
        }
        finally
        {
            if (sw.ElapsedMilliseconds >= WarnThresholdMs)
            {
                var detail = success ? (enabled ? "enabled" : "disabled") : $"error {retCode}";
                log.Warn($"Slow OS call ({sw.ElapsedMilliseconds}ms): NetUserGetInfo({username}) → {detail}");
            }
        }
    }
}

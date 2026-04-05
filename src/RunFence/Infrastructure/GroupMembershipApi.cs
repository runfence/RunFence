using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using RunFence.Core;

namespace RunFence.Infrastructure;

public readonly record struct NetUserGroupsResult(int ReturnCode, IntPtr BufPtr, int EntriesRead);

public class GroupMembershipApi(ILoggingService log)
{
    private const int WarnThresholdMs = 100;

    /// <summary>Used by LocalGroupMembershipService.GetLocalGroups — wraps PrincipalSearcher.FindAll.</summary>
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

    /// <summary>Used by LocalGroupMembershipService.GetMembersOfGroup — wraps GroupPrincipal.Members.</summary>
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

    /// <summary>Used by LocalGroupMembershipService.FetchGroupsForUser and AclPermissionService.TryResolveLocalGroupSids — wraps NetUserGetLocalGroups P/Invoke.</summary>
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

    /// <summary>Used by LocalGroupMembershipService.GetGroupDescription — wraps GroupPrincipal.Description read.</summary>
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
}

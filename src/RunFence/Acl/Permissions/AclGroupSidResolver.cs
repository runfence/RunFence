using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl.Permissions;

public class AclGroupSidResolver(
    NTTranslateApi ntTranslate,
    GroupMembershipApi groupMembership,
    ILocalGroupQueryService localGroupQueryService)
{
    public List<string> ResolveAccountGroupSids(string accountSid)
    {
        if (AclHelper.IsContainerSid(accountSid) || AclHelper.IsLowIntegritySid(accountSid))
            return ["S-1-1-0", AclHelper.AllApplicationPackagesSid];

        var sids = new List<string>
        {
            "S-1-1-0",
            "S-1-5-11",
            "S-1-5-32-545",
        };

        foreach (var groupSid in TryResolveLocalGroupSids(accountSid))
        {
            if (!sids.Contains(groupSid, StringComparer.OrdinalIgnoreCase))
                sids.Add(groupSid);
        }

        return sids;
    }

    private IEnumerable<string> TryResolveLocalGroupSids(string accountSid)
    {
        try
        {
            if (localGroupQueryService.IsLocalGroup(accountSid))
                return [];

            var ntAccount = ntTranslate.TranslateName(accountSid);
            var netApiResult = groupMembership.NetUserGetLocalGroups(ntAccount.Value);

            if (netApiResult.ReturnCode != 0 || netApiResult.BufPtr == IntPtr.Zero)
                return [];

            try
            {
                var result = new List<string>();
                var structSize = Marshal.SizeOf<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>();
                for (var i = 0; i < netApiResult.EntriesRead; i++)
                {
                    var entry = Marshal.PtrToStructure<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>(
                        IntPtr.Add(netApiResult.BufPtr, i * structSize));
                    try
                    {
                        var sid = ntTranslate.TranslateSid(entry.lgrui0_name);
                        result.Add(sid.Value);
                    }
                    catch
                    {
                    }
                }

                return result;
            }
            finally
            {
                GroupMembershipNative.NetApiBufferFree(netApiResult.BufPtr);
            }
        }
        catch
        {
            return [];
        }
    }
}

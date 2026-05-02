using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

public class LocalSamSidResolver(ILoggingService log) : ILocalSamSidResolver
{
    public bool TryGetLocalUserSid(string username, out string sid)
    {
        sid = string.Empty;

        var status = GroupMembershipNative.NetUserGetInfo(null, username, 23, out var bufPtr);
        if (status != LocalSamSidResolverNative.NerrSuccess || bufPtr == IntPtr.Zero)
        {
            if (bufPtr != IntPtr.Zero)
                GroupMembershipNative.NetApiBufferFree(bufPtr);

            log.Warn($"NetUserGetInfo(level 23) failed for local user '{username}': error {status}");
            return false;
        }

        try
        {
            var info = Marshal.PtrToStructure<LocalSamSidResolverNative.USER_INFO_23>(bufPtr);

            if ((info.usri23_flags & GroupMembershipNative.UF_ACCOUNTDISABLE) != 0)
                return false;

            if (info.usri23_user_sid == IntPtr.Zero)
            {
                log.Warn($"NetUserGetInfo(level 23) returned a null SID for local user '{username}'");
                return false;
            }

            if (!LocalSamSidResolverNative.ConvertSidToStringSidW(info.usri23_user_sid, out var stringSidPtr))
            {
                log.Warn($"ConvertSidToStringSidW failed for local user '{username}': error {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                sid = Marshal.PtrToStringUni(stringSidPtr) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sid))
                    return true;

                log.Warn($"ConvertSidToStringSidW returned an empty SID for local user '{username}'");
                return false;
            }
            finally
            {
                ProcessNative.LocalFree(stringSidPtr);
            }
        }
        finally
        {
            GroupMembershipNative.NetApiBufferFree(bufPtr);
        }
    }

    public string GetRequiredLocalUserSid(string username)
    {
        if (TryGetLocalUserSid(username, out var sid))
            return sid;

        throw new InvalidOperationException($"Failed to resolve local SAM SID for account '{username}'.");
    }
}

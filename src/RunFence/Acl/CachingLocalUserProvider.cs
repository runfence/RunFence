using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Caches local Windows user accounts with a 30-second TTL. Thread-safe.
/// Uses NetUserEnum (level 0) for fast local user enumeration and NetUserGetInfo(level 23)
/// to read the SID directly from the local SAM buffer, avoiding per-user name-resolution calls.
/// </summary>
public class CachingLocalUserProvider(ILoggingService log) : ILocalUserProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly object _cacheLock = new();
    private List<LocalUserAccount>? _cachedLocalUsers;
    private DateTime _cacheTimestamp;

    public List<LocalUserAccount> GetLocalUserAccounts()
    {
        lock (_cacheLock)
        {
            if (_cachedLocalUsers != null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
                return _cachedLocalUsers;

            _cachedLocalUsers = EnumerateLocalUsers();
            _cacheTimestamp = DateTime.UtcNow;
            return _cachedLocalUsers;
        }
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedLocalUsers = null;
        }
    }

    private List<LocalUserAccount> EnumerateLocalUsers()
    {
        var users = new List<LocalUserAccount>();
        int resumeHandle = 0;
        int result;

        do
        {
            result = CachingLocalUserProviderNative.NetUserEnum(
                null,
                0,
                CachingLocalUserProviderNative.FilterNormalAccount,
                out var bufPtr,
                -1,
                out var entriesRead,
                out _,
                ref resumeHandle);

            if (bufPtr == IntPtr.Zero)
            {
                if (result != CachingLocalUserProviderNative.NerrSuccess)
                    log.Warn($"NetUserEnum failed: error {result}");
                break;
            }

            try
            {
                if (result is not (CachingLocalUserProviderNative.NerrSuccess or CachingLocalUserProviderNative.ErrorMoreData))
                {
                    log.Warn($"NetUserEnum returned error {result}");
                    break;
                }

                for (var i = 0; i < entriesRead; i++)
                {
                    var entryPtr = IntPtr.Add(bufPtr, i * IntPtr.Size);
                    var namePtr = Marshal.ReadIntPtr(entryPtr);
                    var name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr) : null;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!TryGetLocalUserSid(name, out var sidString))
                        continue;

                    users.Add(new LocalUserAccount(name, sidString));
                }
            }
            finally
            {
                GroupMembershipNative.NetApiBufferFree(bufPtr);
            }
        } while (result == CachingLocalUserProviderNative.ErrorMoreData);

        return users;
    }

    private bool TryGetLocalUserSid(string name, out string sidString)
    {
        sidString = string.Empty;

        var status = GroupMembershipNative.NetUserGetInfo(null, name, 23, out var bufPtr);
        if (status != CachingLocalUserProviderNative.NerrSuccess || bufPtr == IntPtr.Zero)
        {
            if (bufPtr != IntPtr.Zero)
                GroupMembershipNative.NetApiBufferFree(bufPtr);

            log.Warn($"NetUserGetInfo(level 23) failed for user '{name}': error {status}");
            return false;
        }

        try
        {
            var info = Marshal.PtrToStructure<CachingLocalUserProviderNative.USER_INFO_23>(bufPtr);

            if ((info.usri23_flags & GroupMembershipNative.UF_ACCOUNTDISABLE) != 0)
                return false;

            if (info.usri23_user_sid == IntPtr.Zero)
            {
                log.Warn($"NetUserGetInfo(level 23) returned a null SID for user '{name}'");
                return false;
            }

            if (!CachingLocalUserProviderNative.ConvertSidToStringSidW(info.usri23_user_sid, out var stringSidPtr))
            {
                log.Warn($"ConvertSidToStringSidW failed for user '{name}': error {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                sidString = Marshal.PtrToStringUni(stringSidPtr) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(sidString);
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
}
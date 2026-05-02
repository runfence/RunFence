using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Caches local Windows user accounts with a 30-second TTL. Thread-safe.
/// Uses NetUserEnum (level 0) for fast local user enumeration and the shared local SAM SID
/// resolver to avoid per-user name-resolution calls.
/// </summary>
public class CachingLocalUserProvider(ILoggingService log, ILocalSamSidResolver localSamSidResolver) : ILocalUserProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly object _cacheLock = new();
    private IReadOnlyList<LocalUserAccount>? _cachedLocalUsers;
    private DateTime _cacheTimestamp;

    public IReadOnlyList<LocalUserAccount> GetLocalUserAccounts()
    {
        lock (_cacheLock)
        {
            if (_cachedLocalUsers != null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
                return _cachedLocalUsers;

            _cachedLocalUsers = EnumerateLocalUsers().AsReadOnly();
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

                    if (!localSamSidResolver.TryGetLocalUserSid(name, out var sidString))
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
}

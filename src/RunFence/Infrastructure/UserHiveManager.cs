using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;

namespace RunFence.Infrastructure;

public class UserHiveManager(ILoggingService log, RegistryKey? hkuOverride = null) : IUserHiveManager
{
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;

    public IDisposable? EnsureHiveLoaded(string sid)
    {
        if (IsHiveLoaded(sid))
            return null;

        using var profileKey = Registry.LocalMachine.OpenSubKey(
            Constants.ProfileListRegistryKey + "\\" + sid);

        if (profileKey == null)
        {
            log.Warn($"UserHiveManager: Profile not found for SID {sid}");
            return null;
        }

        var profilePath = profileKey.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrEmpty(profilePath))
        {
            log.Warn($"UserHiveManager: ProfileImagePath missing for SID {sid}");
            return null;
        }

        var hivePath = profilePath + "\\NTUSER.DAT";
        if (!File.Exists(hivePath))
        {
            log.Warn($"UserHiveManager: NTUSER.DAT not found at '{hivePath}' for SID {sid}");
            return null;
        }

        var result = RegLoadKeyW(_hku.Handle, sid, hivePath);
        if (result != 0)
        {
            log.Warn($"UserHiveManager: RegLoadKeyW failed for SID {sid}, error {result}");
            return null;
        }

        return new HiveUnloader(_hku, sid, log);
    }

    public bool IsHiveLoaded(string sid)
    {
        using var key = _hku.OpenSubKey(sid);
        return key != null;
    }

    private sealed class HiveUnloader(RegistryKey hku, string sid, ILoggingService log) : IDisposable
    {
        private bool _disposed;

        /// <remarks>
        /// MUST be called on a background thread. <c>GC.WaitForPendingFinalizers()</c> blocks
        /// until all finalizers complete, which can take hundreds of milliseconds and would freeze
        /// the UI thread. <c>GC.Collect</c> + <c>GC.WaitForPendingFinalizers</c> is required here
        /// to flush all open <see cref="RegistryKey"/> handles before <c>RegUnLoadKey</c> is called,
        /// since unfinalized <see cref="RegistryKey"/> objects hold native handles that prevent unload.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            System.Diagnostics.Debug.Assert(
                !System.Threading.SynchronizationContext.Current?.GetType().Name.Contains("WindowsFormsSynchronizationContext") ?? true,
                "HiveUnloader.Dispose must not be called on the UI thread — GC.WaitForPendingFinalizers() would freeze the UI.");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var result = UserHiveManager.RegUnLoadKey(hku.Handle, sid);
            if (result != 0)
                log.Warn($"UserHiveManager: RegUnLoadKey failed for SID {sid}, error {result}");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegLoadKeyW(SafeRegistryHandle hKey, string subKey, string file);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegUnLoadKey(SafeRegistryHandle hKey, string subKey);
}

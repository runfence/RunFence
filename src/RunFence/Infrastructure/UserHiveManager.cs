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
            PathConstants.ProfileListRegistryKey + "\\" + sid);

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
        private int _disposed;

        ~HiveUnloader()
        {
            Dispose(disposing: false);
        }

        /// <remarks>
        /// Explicit disposal forces pending registry-key finalizers to run before <c>RegUnLoadKey</c>,
        /// reducing unload failures from still-open native handles. The finalizer path is best-effort
        /// cleanup only and intentionally skips the forced collection/finalizer wait.
        /// </remarks>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (disposing)
            {
                System.Diagnostics.Debug.Assert(
                    !SynchronizationContext.Current?.GetType().Name.Contains("WindowsFormsSynchronizationContext") ?? true,
                    "HiveUnloader.Dispose must not be called on the UI thread - GC.WaitForPendingFinalizers() would freeze the UI.");

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            var result = RegUnLoadKey(hku.Handle, sid);
            if (result != 0)
                log.Warn($"UserHiveManager: RegUnLoadKey failed for SID {sid}, error {result}");
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegLoadKeyW(SafeRegistryHandle hKey, string subKey, string file);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegUnLoadKey(SafeRegistryHandle hKey, string subKey);
}

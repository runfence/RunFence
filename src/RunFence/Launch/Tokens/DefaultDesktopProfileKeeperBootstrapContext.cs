using RunFence.Core;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace RunFence.Launch.Tokens;

public sealed class DefaultDesktopProfileKeeperBootstrapContext(ILoggingService log) : IProfileKeeperBootstrapContext
{
    public T Run<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        IntPtr desktop = IntPtr.Zero;

        var thread = new Thread(() =>
        {
            desktop = Native.OpenDesktop(
                "Default",
                0,
                false,
                Native.DesktopAllAccess);

            try
            {
                if (desktop == IntPtr.Zero)
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "ProfileKeeper bootstrap could not open WinSta0\\Default.");
                if (!Native.SetThreadDesktop(desktop))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "ProfileKeeper bootstrap could not bind to WinSta0\\Default.");

                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        using (ExecutionContext.SuppressFlow())
        {
            thread.Start();
        }

        thread.Join();

        if (desktop != IntPtr.Zero && !Native.CloseDesktop(desktop))
            log.Warn($"ProfileKeeper bootstrap could not close WinSta0\\Default: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result;
    }

    private static class Native
    {
        public const uint StandardRightsRequired = 0x000F0000;
        public const uint DesktopSpecificAll = 0x01FF;
        public const uint DesktopAllAccess = StandardRightsRequired | DesktopSpecificAll;

        [DllImport("user32.dll", EntryPoint = "OpenDesktopW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenDesktop(
            string desktop,
            uint flags,
            bool inherit,
            uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr desktop);
    }
}

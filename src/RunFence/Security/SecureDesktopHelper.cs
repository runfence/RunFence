using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Security;

public class SecureDesktopHelper(ILoggingService log, ISecureDesktopNative native) : ISecureDesktopRunner
{
    private const uint DesktopAll = 0x01FF;
    private const int UoiName = 2;
    private const uint WmClose = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj,
        int nIndex,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pvInfo,
        int nLength,
        out int lpnLengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumDesktopWindowsProc lpfn, IntPtr lParam);

    private delegate bool EnumDesktopWindowsProc(IntPtr hwnd, IntPtr lParam);

    private static int _active;
    [ThreadStatic] private static bool _inDoEvents;

    void ISecureDesktopRunner.Run(Action action) => RunSecureDesktop(action);

    private void RunSecureDesktop(Action action)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            log.Warn("SecureDesktopHelper: reentrant call denied");
            return;
        }

        var captureResult = native.CaptureOriginalDesktop();
        var originalDesktop = captureResult.OpenedDesktopHandle;
        if (captureResult.Status != SecureDesktopNativeStatus.Succeeded || originalDesktop == IntPtr.Zero)
        {
            log.Warn($"SecureDesktopHelper: OpenInputDesktop failed ({native.FormatNativeError(captureResult.NativeErrorCode)})");
            Interlocked.Exchange(ref _active, 0);
            return;
        }

        var desktopName = "D_" + Guid.NewGuid().ToString("N");
        var createResult = native.CreateSecureDesktop(desktopName);
        var secureDesktop = createResult.OpenedDesktopHandle;
        if (createResult.Status != SecureDesktopNativeStatus.Succeeded || secureDesktop == IntPtr.Zero)
        {
            log.Warn($"SecureDesktopHelper: CreateSecureDesktop failed ({native.FormatNativeError(createResult.NativeErrorCode)})");
            Interlocked.Exchange(ref _active, 0);
            native.CloseDesktop(originalDesktop);
            return;
        }

        try
        {
            var switchResult = native.SwitchDesktop(secureDesktop);
            if (switchResult.Status != SecureDesktopNativeStatus.Succeeded)
            {
                log.Warn($"SecureDesktopHelper: SwitchDesktop failed ({native.FormatNativeError(switchResult.NativeErrorCode)})");
                return;
            }

            Exception? threadException = null;
            var worker = new Thread(() =>
            {
                if (!SetThreadDesktop(secureDesktop))
                {
                    log.Warn($"SecureDesktopHelper: SetThreadDesktop failed (error={Marshal.GetLastWin32Error()})");
                    return;
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });

            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();

            while (worker.IsAlive)
            {
                if (!_inDoEvents)
                {
                    // Keep this message pump loop for secure-desktop responsiveness; it is a deliberate
                    // secure-desktop cleanup mechanism, not a general reentrancy recommendation.
                    _inDoEvents = true;
                    try { Application.DoEvents(); }
                    finally { _inDoEvents = false; }
                }

                worker.Join(50);

                var inputDesktop = OpenInputDesktop(0, false, DesktopAll);
                if (inputDesktop != IntPtr.Zero)
                {
                    var inputName = GetDesktopName(inputDesktop);
                    native.CloseDesktop(inputDesktop);
                    if (inputName != null && !string.Equals(inputName, desktopName, StringComparison.Ordinal))
                        CloseAllWindowsOnDesktop(secureDesktop);
                }
            }

            if (threadException != null)
                ExceptionDispatchInfo.Capture(threadException).Throw();
        }
        finally
        {
            Interlocked.Exchange(ref _active, 0);
            var restoreResult = native.RestoreDesktop(originalDesktop, captureResult.OriginalDesktopIdentity);
            if (restoreResult.Status != SecureDesktopNativeStatus.Succeeded)
                log.Warn($"SecureDesktopHelper: restore failed ({native.FormatNativeError(restoreResult.NativeErrorCode)})");

            var closeSecure = native.CloseDesktop(secureDesktop);
            if (closeSecure.Status != SecureDesktopNativeStatus.Succeeded)
                log.Warn($"SecureDesktopHelper: close secure desktop failed ({native.FormatNativeError(closeSecure.NativeErrorCode)})");

            var closeOriginal = native.CloseDesktop(originalDesktop);
            if (closeOriginal.Status != SecureDesktopNativeStatus.Succeeded)
                log.Warn($"SecureDesktopHelper: close original desktop failed ({native.FormatNativeError(closeOriginal.NativeErrorCode)})");
        }
    }

    private static string? GetDesktopName(IntPtr desktop)
    {
        var sb = new StringBuilder(256);
        return GetUserObjectInformation(desktop, UoiName, sb, sb.Capacity * 2, out _) ? sb.ToString() : null;
    }

    private static void CloseAllWindowsOnDesktop(IntPtr desktop)
    {
        EnumDesktopWindows(desktop, (hwnd, _) =>
        {
            WindowNative.PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
}

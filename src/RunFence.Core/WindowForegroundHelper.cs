using System.Runtime.InteropServices;

namespace RunFence.Core;

/// <summary>
/// Forces a window to the foreground by temporarily disabling the foreground activation lock.
/// Also restores the window if it is minimized at the Win32 level — SetForegroundWindow
/// alone does not restore minimized windows, it only flashes the taskbar button.
/// </summary>
// P/Invoke duplication with WindowNative (RunFence project) is architecturally justified:
// RunFence.Core cannot reference the RunFence project, so window-management P/Invokes
// that are needed here must be declared locally.
public static class WindowForegroundHelper
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

    private const int SW_RESTORE = 9;
    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;

    public static void ForceToForeground(nint hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        // Temporarily disable the foreground lock so SetForegroundWindow succeeds
        // unconditionally. Startup tasks (DI resolution, config decryption) take long enough
        // that the foreground rights granted at process launch expire before the window is
        // shown. AttachThreadInput is not a documented bypass for the foreground lock and is
        // increasingly restricted on Windows 10+. Setting the lock timeout to 0 is the
        // officially documented approach (listed explicitly in the SetForegroundWindow MSDN
        // conditions). Without this, SetForegroundWindow silently fails — the window gets
        // keyboard focus from ShowWindow but never receives WM_ACTIVATE/WM_NCACTIVATE(TRUE),
        // leaving the title bar grey and the window absent from Alt+Tab tracking.
        uint lockTimeout = 0;
        SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref lockTimeout, 0);
        SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, (nint)0, 0);
        try
        {
            SetForegroundWindow(hWnd);
        }
        finally
        {
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, (nint)lockTimeout, 0);
        }
    }
}

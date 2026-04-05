using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// Tracks the last foreground window that belongs to a different process using
/// <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>. Own-process events are filtered at hook level
/// via <c>WINEVENT_SKIPOWNPROCESS</c>, so every stored handle is a non-RunFence window.
/// Transient system UI windows (Alt+Tab switcher, foreground-staging) are excluded so they
/// don't overwrite the last real application window.
/// </summary>
public class PreviousWindowTracker : IPreviousWindowTracker, IDisposable
{
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly NativeInterop.WinEventDelegate _hookCallback;
    private IntPtr _hookHandle;

    public PreviousWindowTracker()
    {
        _hookCallback = OnWinEvent;
        _hookHandle = NativeInterop.SetWinEventHook(
            NativeInterop.EventSystemForeground, NativeInterop.EventSystemForeground,
            IntPtr.Zero, _hookCallback, 0, 0,
            NativeInterop.WinEventOutOfContext | NativeInterop.WinEventSkipOwnProcess);
    }

    public IntPtr PreviousWindow { get; private set; }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint eventThread, uint eventTime)
    {
        if (hwnd == IntPtr.Zero || IsTransientSystemWindow(hwnd))
            return;
        PreviousWindow = hwnd;
    }

    // ForegroundStaging: internal Windows transitional window, always empty title, never a real target.
    // XamlExplorerHostIslandWindow titled "Task Switching": Windows 11 Alt+Tab switcher.
    // Shell_TrayWnd: Windows taskbar, activated transiently when secure desktop closes and the
    //   normal desktop is restored — overwrites the real target window we want to type to.
    private static bool IsTransientSystemWindow(IntPtr hwnd)
    {
        var cls = new StringBuilder(64);
        NativeInterop.GetClassName(hwnd, cls, cls.Capacity);
        var className = cls.ToString();

        switch (className)
        {
            case "ForegroundStaging" or "Shell_TrayWnd":
                return true;
            case "XamlExplorerHostIslandWindow":
            {
                var title = new StringBuilder(64);
                NativeInterop.GetWindowText(hwnd, title, title.Capacity);
                return title.ToString() == "Task Switching";
            }
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeInterop.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
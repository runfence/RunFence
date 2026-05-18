using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public enum AutoTypeResult
{
    Success,
    WindowUnavailable,
    FocusChanged
}

public interface IPasswordAutoTyper
{
    AutoTypeResult TypeToWindow(IntPtr hwnd, ProtectedString password);
}

/// <summary>
/// Types a password into a target window without using the clipboard.
///
/// Win32 GUI targets receive <c>PostMessage(WM_CHAR)</c> directly to the focused child control,
/// bypassing the keyboard input pipeline. Console targets (cmd.exe, PowerShell, Windows Terminal)
/// use <c>SendInput(KEYEVENTF_UNICODE)</c> because they do not process <c>WM_CHAR</c> for text input.
/// Focus is monitored per character; typing stops immediately if the foreground moves to a
/// different process.
/// </summary>
public class PasswordAutoTyper(ILoggingService log) : IPasswordAutoTyper
{
    public AutoTypeResult TypeToWindow(IntPtr targetHwnd, ProtectedString password)
    {
        if (!WindowNative.IsWindow(targetHwnd))
        {
            log.Warn("TypeToWindow: targetHwnd is not a valid window");
            return AutoTypeResult.WindowUnavailable;
        }

        string targetTitle = GetWindowTitle(targetHwnd);
        string targetClass = GetWindowClass(targetHwnd);
        bool isConsole = IsConsoleLikeWindow(targetHwnd, targetClass);
        string titleForLog = targetTitle.Length > 50 ? targetTitle[..50] + "..." : targetTitle;
        log.Info($"TypeToWindow: target hwnd=0x{targetHwnd.ToInt64():X} class=\"{targetClass}\" title=\"{titleForLog}\" isConsole={isConsole}");

        // Restore window if minimized - SetForegroundWindow alone only flashes the taskbar for iconic windows.
        if (WindowNative.IsIconic(targetHwnd))
            WindowNative.ShowWindow(targetHwnd, WindowNative.SW_RESTORE);

        // Attach the current foreground thread to RunFence's UI thread so SetForegroundWindow
        // succeeds even when RunFence lost the foreground lock (e.g. after a PIN or Hello dialog).
        IntPtr foregroundHwnd = WindowNative.GetForegroundWindow();
        uint foregroundThread = WindowNative.GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);
        uint currentThread = WindowNative.GetCurrentThreadId();
        uint targetThread = WindowNative.GetWindowThreadProcessId(targetHwnd, out uint targetPid);

        bool attached = foregroundThread != currentThread &&
                        WindowNative.AttachThreadInput(foregroundThread, currentThread, true);
        bool focused;
        try
        {
            WindowNative.BringWindowToTop(targetHwnd);
            focused = WindowNative.SetForegroundWindow(targetHwnd);
        }
        finally
        {
            if (attached)
                WindowNative.AttachThreadInput(foregroundThread, currentThread, false);
        }

        if (!focused)
            log.Warn($"TypeToWindow: SetForegroundWindow failed (foreground was 0x{foregroundHwnd.ToInt64():X} thread={foregroundThread}, current thread={currentThread})");
        else
            log.Info("TypeToWindow: SetForegroundWindow succeeded");

        if (!focused)
        {
            log.Warn("TypeToWindow: refusing to type because target focus could not be established.");
            return AutoTypeResult.WindowUnavailable;
        }

        Thread.Sleep(50);

        IntPtr originalForegroundHwnd = WindowNative.GetForegroundWindow();
        IntPtr hwndFocus = targetHwnd;
        if (!isConsole)
        {
            var gui = new WindowNative.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<WindowNative.GUITHREADINFO>()
            };
            WindowNative.GetGUIThreadInfo(targetThread, ref gui);
            hwndFocus = gui.hwndFocus != IntPtr.Zero ? gui.hwndFocus : targetHwnd;
            log.Info($"TypeToWindow: typing to hwnd=0x{hwndFocus.ToInt64():X} class=\"{GetWindowClass(hwndFocus)}\" title=\"{GetWindowTitle(hwndFocus)}\"");
        }
        else
        {
            log.Info($"TypeToWindow: typing via SendInput to console hwnd=0x{hwndFocus.ToInt64():X}");
        }

        if (!IsTargetFocusStable(targetHwnd, targetThread, hwndFocus, isConsole))
        {
            log.Info($"TypeToWindow: focus changed before typing started (foreground=0x{originalForegroundHwnd.ToInt64():X}, target=0x{targetHwnd.ToInt64():X}, focus=0x{hwndFocus.ToInt64():X})");
            return AutoTypeResult.FocusChanged;
        }

        return password.UseUnicodeSnapshot(snapshot =>
        {
            IntPtr passwordPointer = snapshot.DangerousGetIntPtr();
            for (int i = 0; i < snapshot.CharCount; i++)
            {
                if (focused && !IsTargetFocusStable(targetHwnd, targetThread, hwndFocus, isConsole))
                {
                    WindowNative.GetWindowThreadProcessId(WindowNative.GetForegroundWindow(), out uint fgPid);
                    log.Info($"TypeToWindow: focus changed after {i} chars (foreground pid={fgPid}, target pid={targetPid})");
                    return AutoTypeResult.FocusChanged;
                }

                ushort ch = (ushort)Marshal.ReadInt16(passwordPointer, i * sizeof(char));

                if (isConsole)
                {
                    var inputs = new[]
                    {
                        new WindowNative.INPUT
                        {
                            type = WindowNative.InputKeyboard,
                            ki = new WindowNative.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = WindowNative.KeyeventfUnicode }
                        },
                        new WindowNative.INPUT
                        {
                            type = WindowNative.InputKeyboard,
                            ki = new WindowNative.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = WindowNative.KeyeventfUnicode | WindowNative.KeyeventfKeyup }
                        }
                    };
                    if (WindowNative.SendInput(2, inputs, Marshal.SizeOf<WindowNative.INPUT>()) == 0)
                    {
                        log.Warn($"TypeToWindow: SendInput failed at char {i}");
                        return AutoTypeResult.WindowUnavailable;
                    }
                }
                else
                {
                    if (!WindowNative.PostMessage(hwndFocus, WindowNative.WmChar, ch, IntPtr.Zero))
                    {
                        log.Warn($"TypeToWindow: PostMessage(WM_CHAR) failed at char {i}");
                        return AutoTypeResult.WindowUnavailable;
                    }
                }
            }

            log.Info("TypeToWindow: typed all chars successfully");
            return AutoTypeResult.Success;
        });
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        WindowNative.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        WindowNative.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsConsoleLikeWindow(IntPtr hwnd, string className)
    {
        if (string.Equals(className, WindowNative.ConsoleWindowClass, StringComparison.OrdinalIgnoreCase))
            return true;

        WindowNative.GetWindowThreadProcessId(hwnd, out uint pid);
        using var handle = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
            return false;

        uint size = 512;
        var sb = new StringBuilder((int)size);
        if (!ProcessNative.QueryFullProcessImageName(handle, 0, sb, ref size))
            return false;

        string processName = Path.GetFileNameWithoutExtension(sb.ToString());
        return processName.Contains("WindowsTerminal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetFocusStable(IntPtr targetHwnd, uint targetThread, IntPtr expectedFocusHwnd, bool isConsole)
    {
        IntPtr foregroundHwnd = WindowNative.GetForegroundWindow();
        if (foregroundHwnd != targetHwnd)
            return false;

        uint foregroundThread = WindowNative.GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);
        if (foregroundThread != targetThread)
            return false;

        if (isConsole)
            return true;

        var gui = new WindowNative.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<WindowNative.GUITHREADINFO>()
        };
        if (!WindowNative.GetGUIThreadInfo(targetThread, ref gui))
            return false;

        IntPtr currentFocus = gui.hwndFocus != IntPtr.Zero ? gui.hwndFocus : targetHwnd;
        return currentFocus == expectedFocusHwnd;
    }
}

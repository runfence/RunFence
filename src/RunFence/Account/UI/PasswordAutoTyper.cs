using System.Runtime.InteropServices;
using System.Security;
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
    AutoTypeResult TypeToWindow(IntPtr hwnd, SecureString password);
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
public class PasswordAutoTyper : IPasswordAutoTyper
{
    private readonly ILoggingService _log;

    public PasswordAutoTyper(ILoggingService log) => _log = log;

    public AutoTypeResult TypeToWindow(IntPtr targetHwnd, SecureString password)
    {
        if (!NativeInterop.IsWindow(targetHwnd))
        {
            _log.Warn("TypeToWindow: targetHwnd is not a valid window");
            return AutoTypeResult.WindowUnavailable;
        }

        var targetTitle = GetWindowTitle(targetHwnd);
        var targetClass = GetWindowClass(targetHwnd);
        var isConsole = IsConsoleLikeWindow(targetHwnd, targetClass);
        _log.Info($"TypeToWindow: target hwnd=0x{targetHwnd.ToInt64():X} class=\"{targetClass}\" title=\"{targetTitle}\" isConsole={isConsole}");

        // Restore window if minimized — SetForegroundWindow alone only flashes the taskbar for iconic windows.
        if (NativeInterop.IsIconic(targetHwnd))
            NativeInterop.ShowWindow(targetHwnd, NativeInterop.SW_RESTORE);

        // Attach the current foreground thread to RunFence's UI thread so SetForegroundWindow
        // succeeds even when RunFence lost the foreground lock (e.g. after a PIN or Hello dialog).
        var foregroundHwnd = NativeInterop.GetForegroundWindow();
        var foregroundThread = NativeInterop.GetWindowThreadProcessId(foregroundHwnd, IntPtr.Zero);
        var currentThread = NativeInterop.GetCurrentThreadId();
        var targetThread = NativeInterop.GetWindowThreadProcessId(targetHwnd, out uint targetPid);

        bool attached = foregroundThread != currentThread &&
                        NativeInterop.AttachThreadInput(foregroundThread, currentThread, true);
        bool focused;
        try
        {
            NativeInterop.BringWindowToTop(targetHwnd);
            focused = NativeInterop.SetForegroundWindow(targetHwnd);
        }
        finally
        {
            if (attached)
                NativeInterop.AttachThreadInput(foregroundThread, currentThread, false);
        }

        if (!focused)
            _log.Warn($"TypeToWindow: SetForegroundWindow failed (foreground was 0x{foregroundHwnd.ToInt64():X} thread={foregroundThread}, current thread={currentThread})");
        else
            _log.Info("TypeToWindow: SetForegroundWindow succeeded");

        if (!focused && isConsole)
            return AutoTypeResult.WindowUnavailable;

        Thread.Sleep(50);

        IntPtr hwndFocus = targetHwnd;
        if (!isConsole)
        {
            var gui = new NativeInterop.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<NativeInterop.GUITHREADINFO>()
            };
            NativeInterop.GetGUIThreadInfo(targetThread, ref gui);
            hwndFocus = gui.hwndFocus != IntPtr.Zero ? gui.hwndFocus : targetHwnd;
            _log.Info($"TypeToWindow: typing to hwnd=0x{hwndFocus.ToInt64():X} class=\"{GetWindowClass(hwndFocus)}\" title=\"{GetWindowTitle(hwndFocus)}\"");
        }
        else
        {
            _log.Info($"TypeToWindow: typing via SendInput to console hwnd=0x{hwndFocus.ToInt64():X}");
        }

        var ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            for (int i = 0; i < password.Length; i++)
            {
                if (focused)
                {
                    NativeInterop.GetWindowThreadProcessId(NativeInterop.GetForegroundWindow(), out uint fgPid);
                    if (fgPid != targetPid)
                    {
                        _log.Info($"TypeToWindow: focus changed after {i} chars (foreground pid={fgPid}, target pid={targetPid})");
                        return AutoTypeResult.FocusChanged;
                    }
                }

                var ch = (ushort)Marshal.ReadInt16(ptr, i * 2);

                if (isConsole)
                {
                    var inputs = new[]
                    {
                        new NativeInterop.INPUT
                            { type = NativeInterop.InputKeyboard, ki = new NativeInterop.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = NativeInterop.KeyeventfUnicode } },
                        new NativeInterop.INPUT
                        {
                            type = NativeInterop.InputKeyboard,
                            ki = new NativeInterop.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = NativeInterop.KeyeventfUnicode | NativeInterop.KeyeventfKeyup }
                        }
                    };
                    if (NativeInterop.SendInput(2, inputs, Marshal.SizeOf<NativeInterop.INPUT>()) == 0)
                    {
                        _log.Warn($"TypeToWindow: SendInput failed at char {i}");
                        return AutoTypeResult.WindowUnavailable;
                    }
                }
                else
                {
                    if (!NativeInterop.PostMessage(hwndFocus, NativeInterop.WmChar, ch, IntPtr.Zero))
                    {
                        _log.Warn($"TypeToWindow: PostMessage(WM_CHAR) failed at char {i}");
                        return AutoTypeResult.WindowUnavailable;
                    }
                }
            }

            _log.Info($"TypeToWindow: typed {password.Length} chars successfully");
            return AutoTypeResult.Success;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeInterop.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        NativeInterop.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsConsoleLikeWindow(IntPtr hwnd, string className)
    {
        if (string.Equals(className, NativeInterop.ConsoleWindowClass, StringComparison.OrdinalIgnoreCase))
            return true;

        NativeInterop.GetWindowThreadProcessId(hwnd, out uint pid);
        using var handle = NativeInterop.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
            return false;

        uint size = 512;
        var sb = new StringBuilder((int)size);
        if (!NativeInterop.QueryFullProcessImageName(handle, 0, sb, ref size))
            return false;

        var processName = Path.GetFileNameWithoutExtension(sb.ToString());
        return processName.Contains("WindowsTerminal", StringComparison.OrdinalIgnoreCase);
    }
}
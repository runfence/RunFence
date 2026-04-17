using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// P/Invoke declarations for window management, message filtering, icon management,
/// shell hook, keyboard/WinEvent hooks, keyboard input simulation, and cursor APIs.
/// Consumed by UI, drag-drop, media bridge, hotkey, and security features.
/// </summary>
public static class WindowNative
{
    // ── User32 (icon management) ──────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── User32 (UIPI message filter) ──────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ChangeWindowMessageFilterEx(
        IntPtr hWnd, uint msg, uint action, IntPtr changeInfo);

    public const uint MSGFLT_ALLOW = 1;
    public const uint MSGFLT_DISALLOW = 2;
    public const uint WM_COPYGLOBALDATA = 0x0049; // undocumented OLE internal
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_DROPFILES = 0x0233;
    public const uint WM_GETOBJECT = 0x003D;

    /// <summary>
    /// Allows OLE drag-and-drop messages from lower-IL processes to reach the given HWND.
    /// Must be called after the control handle is created.
    /// </summary>
    public static void AllowDropFromLowIL(IntPtr hwnd)
    {
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    // ── User32 (shell hook) ───────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DeregisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    /// <summary>Shell hook code posted when a WM_APPCOMMAND reaches the shell.</summary>
    public const int HSHELL_APPCOMMAND = 12;

    // ── Kernel32 (module handle) ──────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── User32 (low-level keyboard hook) ─────────────────────────────────────

    public const int WH_KEYBOARD_LL = 13;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ── User32 (WinEvent hook) ────────────────────────────────────────────────

    public delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate proc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public const uint EventSystemForeground = 0x0003u;
    public const uint WinEventOutOfContext = 0x0000u;
    public const uint WinEventSkipOwnProcess = 0x0002u;

    // ── User32 (window management) ────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    // Overload for thread ID only (no PID): used by DragBridgeWindow and similar callers
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    // Overload that returns thread ID and fills processId out parameter
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public const int SW_RESTORE = 9;

    // ── User32 (window enumeration) ───────────────────────────────────────────

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    // ── User32 (keyboard input simulation) ───────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Sequential layout matches Win32 INPUT union at the same offset as KEYBDINPUT.
    // _padding accounts for MOUSEINPUT being larger than KEYBDINPUT on both x86 and x64,
    // yielding Marshal.SizeOf<INPUT>() == 28 (x86) or 40 (x64) — the value cbSize must equal.
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private long _padding;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint WmChar = 0x0102u;
    public const uint InputKeyboard = 1u;
    public const uint KeyeventfKeyup = 0x0002u;
    public const uint KeyeventfUnicode = 0x0004u;
    public const string ConsoleWindowClass = "ConsoleWindowClass";
}

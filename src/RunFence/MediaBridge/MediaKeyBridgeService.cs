using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Infrastructure;
using Timer = System.Threading.Timer;

namespace RunFence.MediaBridge;

/// <summary>
/// Intercepts the Play/Pause media key via two complementary mechanisms:
///
/// 1. WH_KEYBOARD_LL — catches physical Play/Pause key presses. When the foreground window
///    belongs to a managed/sandboxed account and no interactive-user audio is active,
///    Space is posted to the foreground window so the browser handles play/pause locally.
///
/// 2. RegisterShellHookWindow — catches APPCOMMAND_MEDIA_PLAY_PAUSE routed via RegisterHotKey
///    or direct PostMessage(Shell_TrayWnd, WM_APPCOMMAND), which bypass WH_KEYBOARD_LL entirely.
///    Deduplication prevents double-handling when a key arrives via both paths.
/// </summary>
public class MediaKeyBridgeService : IMediaKeyBridgeService, IRequiresInitialization
{
    private const uint LLKHF_INJECTED = 0x10;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const uint VK_SPACE = 0x20;
    private const uint SpaceScanCode = 0x39;

    private readonly WindowNative.LowLevelKeyboardProc _hookProc; // kept alive to prevent GC
    private readonly ILoggingService _log;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly ICoreAudioSessionChecker _audioChecker;
    private IntPtr _hook;
    private ShellHookWindow? _shellHookWindow;

    // Polled every 2 seconds by _pollTimer; read on the UI/hook thread.
    private volatile bool _interactiveUserPlaying;
    private Timer? _pollTimer;

    // Deduplication: prevents the shell hook from re-bridging a key that the WH_KEYBOARD_LL
    // hook already bridged. Both callbacks execute on the main UI thread — no synchronization needed.
    private long _lastBridgedAt; // Environment.TickCount64
    private const long BridgeDedupWindowMs = 200;

    public MediaKeyBridgeService(ILoggingService log, IUiThreadInvoker uiThreadInvoker,
        ICoreAudioSessionChecker audioChecker)
    {
        _log = log;
        _uiThreadInvoker = uiThreadInvoker;
        _audioChecker = audioChecker;
        _hookProc = HookCallback;
    }

    public void Initialize()
    {
        _log.Info("MediaKeyBridgeService: installing keyboard hook.");
        _hook = WindowNative.SetWindowsHookEx(WindowNative.WH_KEYBOARD_LL, _hookProc, WindowNative.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Warn("MediaKeyBridgeService: Failed to install keyboard hook.");
        else
            _log.Info("MediaKeyBridgeService: keyboard hook installed.");

        _shellHookWindow = new ShellHookWindow(_log, OnShellAppCommand);
        _shellHookWindow.Create();

        _pollTimer = new Timer(PollAudioSession, null,
            TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;

        _shellHookWindow?.Destroy();
        _shellHookWindow = null;

        if (_hook != IntPtr.Zero)
        {
            WindowNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void PollAudioSession(object? state)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(interactiveSid))
        {
            if (_interactiveUserPlaying)
            {
                _interactiveUserPlaying = false;
                _log.Info("MediaKeyBridge: interactiveUserPlaying changed → False");
            }

            return;
        }

        // Invoke (not BeginInvoke) is required: ICoreAudioSessionChecker uses COM interfaces that must
        // be called on the STA UI thread, and the result must be read before the timer callback can return.
        bool playing = false;
        try
        {
            _uiThreadInvoker.Invoke(() => { playing = _audioChecker.IsAnySessionActive(interactiveSid); });
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (playing != _interactiveUserPlaying)
        {
            _interactiveUserPlaying = playing;
            _log.Info($"MediaKeyBridge: interactiveUserPlaying changed → {playing}");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return WindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);

        var msg = (uint)wParam.ToInt64();
        if (msg != WindowNative.WM_KEYDOWN && msg != WindowNative.WM_SYSKEYDOWN)
            return WindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<WindowNative.KBDLLHOOKSTRUCT>(lParam);
        if (info.vkCode != VK_MEDIA_PLAY_PAUSE)
            return WindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);

        // Let injected media keys pass through unmodified.
        if ((info.flags & LLKHF_INJECTED) != 0)
            return WindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);

        _log.Info($"MediaKeyBridge: VK=0x{info.vkCode:X2} interactiveUserPlaying={_interactiveUserPlaying}");

        return TryBridgePlayPause()
            ? 1
            : WindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnShellAppCommand(int appCommand)
    {
        if (appCommand != APPCOMMAND_MEDIA_PLAY_PAUSE)
            return;

        // Skip if the WH_KEYBOARD_LL hook already bridged within the dedup window.
        if (Environment.TickCount64 - _lastBridgedAt < BridgeDedupWindowMs)
        {
            _log.Info($"MediaKeyBridge: shell APPCOMMAND={appCommand} dedup-skipped");
            return;
        }

        _log.Info($"MediaKeyBridge: shell APPCOMMAND={appCommand} interactiveUserPlaying={_interactiveUserPlaying}");
        TryBridgePlayPause();
    }

    private bool TryBridgePlayPause()
    {
        if (_interactiveUserPlaying)
        {
            _log.Info("MediaKeyBridge: interactive user is playing → pass through");
            return false;
        }

        var hwnd = WindowNative.GetForegroundWindow();
        _log.Info($"MediaKeyBridge: foregroundHwnd=0x{hwnd.ToInt64():X}");
        if (hwnd == IntPtr.Zero)
            return false;

        uint threadId = WindowNative.GetWindowThreadProcessId(hwnd, out uint pid);
        _log.Info($"MediaKeyBridge: foregroundPid={pid}");
        if (pid == 0)
            return false;

        var ownerSid = NativeTokenHelper.TryGetProcessOwnerSid(pid);
        _log.Info($"MediaKeyBridge: foregroundOwnerSid={ownerSid?.Value ?? "null"}");
        if (ownerSid == null)
            return false;

        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        _log.Info($"MediaKeyBridge: interactiveSid={interactiveSid ?? "null"}");
        if (string.IsNullOrEmpty(interactiveSid))
            return false;

        // Foreground window owned by the interactive user — let SMTC handle it.
        if (string.Equals(ownerSid.Value, interactiveSid, StringComparison.OrdinalIgnoreCase))
        {
            _log.Info("MediaKeyBridge: foreground owned by interactive user → pass through");
            return false;
        }

        // Check if the thread has an active text caret — Space would type a character rather
        // than triggering play/pause. hwndCaret != Zero means a text insertion point is active.
        if (threadId != 0 && HasActiveCaret(threadId))
        {
            _log.Info("MediaKeyBridge: active text caret in foreground thread → pass through");
            return false;
        }

        // Check if the focused control is a text field or button — Space would type a character
        // or activate the button rather than triggering play/pause.
        if (threadId != 0 && IsFocusedControlTextOrButton(threadId))
        {
            _log.Info("MediaKeyBridge: focused control is text field or button → pass through");
            return false;
        }

        _lastBridgedAt = Environment.TickCount64;

        // Limitation: Win32 GUITHREADINFO cannot detect focus inside browser-internal UI (e.g. the
        // address bar, which lives inside the renderer process). If the user has the browser address bar
        // focused, Space injection types a space character instead of toggling play/pause.
        // This is a narrow edge case: sandboxed browser + no interactive-user audio + address bar focused.

        // Post Space key to the sandboxed browser window. Browsers handle WM_KEYDOWN/WM_KEYUP
        // with VK_SPACE directly in their renderer for play/pause without needing SMTC registration.
        WindowNative.PostMessage(hwnd, WindowNative.WM_KEYDOWN, (IntPtr)VK_SPACE, (IntPtr)((SpaceScanCode << 16) | 1));
        WindowNative.PostMessage(hwnd, WindowNative.WM_KEYUP, (IntPtr)VK_SPACE, unchecked((nint)(uint)(0xC0000000 | (SpaceScanCode << 16) | 1)));
        _log.Info($"MediaKeyBridge: posted Space to hwnd=0x{hwnd.ToInt64():X} pid={pid}");
        return true;
    }

    private static bool HasActiveCaret(uint threadId)
    {
        var info = new WindowNative.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<WindowNative.GUITHREADINFO>()
        };
        return WindowNative.GetGUIThreadInfo(threadId, ref info) && info.hwndCaret != IntPtr.Zero;
    }

    private static bool IsFocusedControlTextOrButton(uint threadId)
    {
        var info = new WindowNative.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<WindowNative.GUITHREADINFO>()
        };
        if (!WindowNative.GetGUIThreadInfo(threadId, ref info))
            return false;

        if (info.hwndFocus == IntPtr.Zero)
            return false;

        var sb = new StringBuilder(256);
        WindowNative.GetClassName(info.hwndFocus, sb, sb.Capacity);
        var className = sb.ToString();

        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
               || className.Equals("Button", StringComparison.OrdinalIgnoreCase)
               || className.Equals("ListBox", StringComparison.OrdinalIgnoreCase)
               || className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)
               || className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase)
               || className.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)
               || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Message-only window that receives HSHELL_APPCOMMAND notifications from the shell.
    /// Covers media keys routed via RegisterHotKey or PostMessage(Shell_TrayWnd, WM_APPCOMMAND)
    /// that never reach the WH_KEYBOARD_LL hook.
    /// </summary>
    private sealed class ShellHookWindow(ILoggingService log, Action<int> onAppCommand) : NativeWindow
    {
        // RegisterWindowMessage("SHELLHOOK") returns the same ID for the life of the process.
        private static readonly uint WmShellHook = WindowNative.RegisterWindowMessage("SHELLHOOK");

        public void Create()
        {
            if (WmShellHook == 0)
            {
                log.Warn("MediaKeyBridge: RegisterWindowMessage(SHELLHOOK) failed — shell hook path disabled.");
                return;
            }

            var cp = new CreateParams { Parent = new IntPtr(-3) }; // HWND_MESSAGE
            CreateHandle(cp);

            // Allow the shell (medium-IL) to post the shell hook message to this high-IL window.
            WindowNative.ChangeWindowMessageFilterEx(Handle, WmShellHook, WindowNative.MSGFLT_ALLOW, IntPtr.Zero);

            if (!WindowNative.RegisterShellHookWindow(Handle))
                log.Warn("MediaKeyBridge: RegisterShellHookWindow failed.");
            else
                log.Info("MediaKeyBridge: shell hook window registered.");
        }

        public void Destroy()
        {
            if (Handle != IntPtr.Zero)
            {
                WindowNative.DeregisterShellHookWindow(Handle);
                DestroyHandle();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmShellHook && m.WParam.ToInt32() == WindowNative.HSHELL_APPCOMMAND)
            {
                // lParam carries the WM_APPCOMMAND lParam format: HIWORD & 0x0FFF = APPCOMMAND value.
                int appCmd = (int)((m.LParam.ToInt64() >> 16) & 0x0FFF);
                onAppCommand(appCmd);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
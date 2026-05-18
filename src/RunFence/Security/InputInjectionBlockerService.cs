using System.Runtime.InteropServices;
using System.Windows.Forms;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Security;

public class InputInjectionBlockerService : IInputInjectionBlockerService, IDisposable
{
    private enum OverrideState
    {
        None,
        DisabledTemporarily,
        DisabledTimed
    }

    private readonly ILoggingService _log;
    private readonly ILowLevelHookApi _hookApi;
    private readonly IForegroundWindowResolver _foregroundWindowResolver;
    private readonly IProcessOwnerSidReader _processOwnerSidReader;
    private readonly IUiTimerFactory _timerFactory;
    private readonly WindowNative.LowLevelKeyboardProc _keyboardProc;
    private readonly WindowNative.LowLevelMouseProc _mouseProc;

    private bool _configEnabled = true;
    private OverrideState _overrideState = OverrideState.None;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private IUiTimer? _timer;
    private HashSet<string> _exemptedSids = [];

    public bool IsEnabled => _configEnabled && _overrideState == OverrideState.None;

    public InputInjectionBlockerService(
        ILoggingService log,
        ILowLevelHookApi hookApi,
        IForegroundWindowResolver foregroundWindowResolver,
        IProcessOwnerSidReader processOwnerSidReader,
        IUiTimerFactory timerFactory)
    {
        _log = log;
        _hookApi = hookApi;
        _foregroundWindowResolver = foregroundWindowResolver;
        _processOwnerSidReader = processOwnerSidReader;
        _timerFactory = timerFactory;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public void ApplyConfigSetting(bool blockInputInjection)
    {
        _configEnabled = blockInputInjection;
        UpdateHookState();
    }

    public void SetTemporarilyDisabled()
    {
        StopAndDisposeTimer();
        _overrideState = OverrideState.DisabledTemporarily;
        UpdateHookState();
    }

    public void SetTimedDisable(TimeSpan duration)
    {
        StopAndDisposeTimer();
        _overrideState = OverrideState.DisabledTimed;
        _timer = _timerFactory.Create();
        _timer.Interval = (int)duration.TotalMilliseconds;
        _timer.Tick += (_, _) => ReEnable();
        _timer.Start();
        UpdateHookState();
    }

    public void ReEnable()
    {
        _overrideState = OverrideState.None;
        StopAndDisposeTimer();
        UpdateHookState();
    }

    public void UpdateExemptedSids(IReadOnlyCollection<string> sids)
    {
        _exemptedSids = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsLowerIlInjected(uint flags) => (flags & 0x02) != 0;

    private void UpdateHookState()
    {
        if (IsEnabled)
            InstallMissingHooks();
        else
            UnhookInstalledHooks();
    }

    private void InstallMissingHooks()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = _hookApi.InstallKeyboardHook(_keyboardProc);
            if (_keyboardHook == IntPtr.Zero)
                _log.Warn("InputInjectionBlockerService: Failed to install keyboard hook.");
        }

        if (_mouseHook == IntPtr.Zero)
        {
            _mouseHook = _hookApi.InstallMouseHook(_mouseProc);
            if (_mouseHook == IntPtr.Zero)
                _log.Warn("InputInjectionBlockerService: Failed to install mouse hook.");
        }
    }

    private void UnhookInstalledHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            _hookApi.Unhook(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            _hookApi.Unhook(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<WindowNative.KBDLLHOOKSTRUCT>(lParam);
            if (IsLowerIlInjected(info.flags))
            {
                var pid = GetForegroundWindowPid();
                var allow = false;
                switch (info.vkCode)
                {
                    case (uint)Keys.MediaPlayPause:
                    case (uint)Keys.MediaPreviousTrack:
                    case (uint)Keys.MediaNextTrack:
                    case (uint)Keys.MediaStop:
                    case (uint)Keys.VolumeDown:
                    case (uint)Keys.VolumeUp:
                    case (uint)Keys.VolumeMute:
                    case (uint)Keys.SelectMedia:
                    case (uint)Keys.RWin:
                    case (uint)Keys.LWin:
                    case (uint)Keys.Menu:
                    case (uint)Keys.LMenu:
                    case (uint)Keys.RMenu:
                    case (uint)Keys.PageUp:
                    case (uint)Keys.PageDown:
                    case (uint)Keys.Pause:
                    case (uint)Keys.Play:
                    case (uint)Keys.Zoom:
                        allow = true;
                        break;
                }

                allow = allow || IsExemptedProcess(pid);

                if (allow)
                    return _hookApi.CallNextHook(_keyboardHook, nCode, wParam, lParam);

                _log.Debug($"InputInjectionBlockerService: Blocked lower-IL keyboard injection. Key: {(Keys)info.vkCode} (0x{info.vkCode:X2})");
                return new IntPtr(1);
            }
        }

        return _hookApi.CallNextHook(_keyboardHook, nCode, wParam, lParam);
    }

    private uint GetForegroundWindowPid() => _foregroundWindowResolver.GetForegroundWindow().ProcessId;

    private bool IsExemptedProcess(uint pid)
    {
        if (_exemptedSids.Count == 0 || pid == 0)
            return false;

        var sid = _processOwnerSidReader.TryGetProcessOwnerSid(pid);
        return sid != null && _exemptedSids.Contains(sid);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<WindowNative.MSLLHOOKSTRUCT>(lParam);
            if (IsLowerIlInjected(info.flags))
            {
                var pid = GetForegroundWindowPid();
                if (IsExemptedProcess(pid))
                    return _hookApi.CallNextHook(_mouseHook, nCode, wParam, lParam);

                _log.Debug("InputInjectionBlockerService: Blocked lower-IL mouse injection.");
                return new IntPtr(1);
            }
        }

        return _hookApi.CallNextHook(_mouseHook, nCode, wParam, lParam);
    }

    private void StopAndDisposeTimer()
    {
        if (_timer == null)
            return;

        _timer.Stop();
        _timer.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        UnhookInstalledHooks();
        StopAndDisposeTimer();
    }
}

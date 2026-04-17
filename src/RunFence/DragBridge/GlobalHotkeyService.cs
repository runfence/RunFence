using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;

namespace RunFence.DragBridge;

/// <summary>
/// Detects global hotkeys using a WH_KEYBOARD_LL low-level keyboard hook.
/// Unlike RegisterHotKey, this approach rejects keystrokes injected by lower-integrity
/// processes (LLKHF_LOWER_IL_INJECTED), preventing non-admin apps from simulating hotkeys
/// against this elevated process via SendInput.
/// </summary>
public class GlobalHotkeyService : IGlobalHotkeyService, IRequiresInitialization
{
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    private const uint WM_SYSKEYUP = 0x0105;

    private readonly record struct HotkeyEntry(int Id, int Modifiers, int Vk, bool Consume);

    private readonly List<HotkeyEntry> _hotkeys = [];
    private readonly InfraWindowNative.LowLevelKeyboardProc _hookProc; // must be kept alive to prevent GC
    private IntPtr _hook;
    private int _currentModifiers;
    private readonly ILoggingService _log;

    public event Action<int>? HotkeyPressed;

    public GlobalHotkeyService(ILoggingService log)
    {
        _log = log;
        _hookProc = HookCallback;
    }

    public void Initialize()
    {
        _log.Info("GlobalHotkeyService: installing keyboard hook.");
        _hook = InfraWindowNative.SetWindowsHookEx(InfraWindowNative.WH_KEYBOARD_LL, _hookProc, InfraWindowNative.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Warn("GlobalHotkeyService: Failed to install keyboard hook.");
        else
            _log.Info("GlobalHotkeyService: keyboard hook installed.");
    }

    public bool Register(int id, int modifiers, int key, bool consume = true)
    {
        Unregister(id);
        _hotkeys.Add(new HotkeyEntry(id, modifiers, key, consume));
        return true;
    }

    public void Unregister(int id) => _hotkeys.RemoveAll(h => h.Id == id);

    public void UnregisterAll() => _hotkeys.Clear();

    public void Dispose()
    {
        UnregisterAll();
        if (_hook != IntPtr.Zero)
        {
            InfraWindowNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<InfraWindowNative.KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLKHF_LOWER_IL_INJECTED) == 0)
            {
                // Re-sync modifier state from OS on every keystroke. This prevents
                // _currentModifiers from getting stuck when modifier keyups are missed
                // (e.g. keys released on the Windows lock screen secure desktop).
                _currentModifiers = GetLiveModifiers();
                if (ProcessKeystroke((uint)wParam, info.vkCode))
                    return 1; // consume the keystroke (matches RegisterHotKey behavior)
            }
        }

        return InfraWindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static int GetLiveModifiers()
    {
        int mods = 0;
        if ((InfraWindowNative.GetAsyncKeyState(0x11) & 0x8000) != 0)
            mods |= 0x0002; // VK_CONTROL
        if ((InfraWindowNative.GetAsyncKeyState(0x12) & 0x8000) != 0)
            mods |= 0x0001; // VK_MENU (Alt)
        if ((InfraWindowNative.GetAsyncKeyState(0x10) & 0x8000) != 0)
            mods |= 0x0004; // VK_SHIFT
        if ((InfraWindowNative.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
            (InfraWindowNative.GetAsyncKeyState(0x5C) & 0x8000) != 0)
            mods |= 0x0008; // VK_LWIN/VK_RWIN
        return mods;
    }

    /// <returns>True if the keystroke matched a registered hotkey and should be consumed.</returns>
    public bool ProcessKeystroke(uint message, uint vkCode)
    {
        bool isKeyDown = message is InfraWindowNative.WM_KEYDOWN or InfraWindowNative.WM_SYSKEYDOWN;
        bool isKeyUp = message is InfraWindowNative.WM_KEYUP or WM_SYSKEYUP;

        int modBit = VkToModBit((int)vkCode);
        if (modBit != 0)
        {
            if (isKeyDown)
                _currentModifiers |= modBit;
            else if (isKeyUp)
                _currentModifiers &= ~modBit;
            return false;
        }

        if (isKeyDown)
        {
            var vk = (int)vkCode;
            foreach (var h in _hotkeys)
            {
                if (h.Vk == vk && h.Modifiers == _currentModifiers)
                {
                    OnHotkey(h.Id);
                    return h.Consume;
                }
            }
        }

        return false;
    }

    private static int VkToModBit(int vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => 0x0002, // VK_CONTROL / VK_LCONTROL / VK_RCONTROL → MOD_CONTROL
        0x12 or 0xA4 or 0xA5 => 0x0001, // VK_MENU / VK_LMENU / VK_RMENU → MOD_ALT
        0x10 or 0xA0 or 0xA1 => 0x0004, // VK_SHIFT / VK_LSHIFT / VK_RSHIFT → MOD_SHIFT
        0x5B or 0x5C => 0x0008, // VK_LWIN / VK_RWIN → MOD_WIN
        _ => 0
    };

    private void OnHotkey(int id) => HotkeyPressed?.Invoke(id);

    /// <summary>Triggers <see cref="HotkeyPressed"/> directly — for unit testing only.</summary>
    public void SimulateHotkey(int id) => OnHotkey(id);
}
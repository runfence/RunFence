using System.Runtime.InteropServices;
using RunFence.Core;
using InfraWindowNative = RunFence.Infrastructure.WindowNative;

namespace RunFence.DragBridge;

public sealed class LowLevelKeyboardHotkeySource : IHotkeySource
{
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    private const uint WM_SYSKEYUP = 0x0105;
    private readonly record struct HotkeyEntry(int Id, int Modifiers, int Vk, bool Consume);

    private readonly List<HotkeyEntry> _hotkeys = [];
    private readonly InfraWindowNative.LowLevelKeyboardProc _hookProc;
    private readonly ILoggingService _log;
    private IntPtr _hook;
    private int _currentModifiers;

    public event Action<int>? HotkeyPressed;

    public LowLevelKeyboardHotkeySource(ILoggingService log)
    {
        _log = log;
        _hookProc = HookCallback;
        _hook = InfraWindowNative.SetWindowsHookEx(InfraWindowNative.WH_KEYBOARD_LL, _hookProc, InfraWindowNative.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Warn("LowLevelKeyboardHotkeySource: Failed to install keyboard hook.");
    }

    public HotkeyRegistrationResult RegisterHotkey(int id, int modifiers, int key, bool consume)
    {
        UnregisterHotkey(id);
        _hotkeys.Add(new HotkeyEntry(id, modifiers, key, consume));
        return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Succeeded, id, modifiers, key, null);
    }

    public HotkeyRegistrationResult UnregisterHotkey(int id)
    {
        int removed = _hotkeys.RemoveAll(h => h.Id == id);
        return removed > 0
            ? new HotkeyRegistrationResult(HotkeyRegistrationStatus.Succeeded, id, 0, 0, null)
            : new HotkeyRegistrationResult(HotkeyRegistrationStatus.NotRegistered, id, 0, 0, null);
    }

    public void Dispose()
    {
        _hotkeys.Clear();
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
                _currentModifiers = GetLiveModifiers();
                if (ProcessKeystroke((uint)wParam, info.vkCode))
                    return 1;
            }
        }

        return InfraWindowNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ProcessKeystroke(uint message, uint vkCode)
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

        if (!isKeyDown)
            return false;

        var vk = (int)vkCode;
        foreach (var h in _hotkeys)
        {
            if (h.Vk == vk && h.Modifiers == _currentModifiers)
            {
                HotkeyPressed?.Invoke(h.Id);
                return h.Consume;
            }
        }

        return false;
    }

    private static int VkToModBit(int vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => 0x0002,
        0x12 or 0xA4 or 0xA5 => 0x0001,
        0x10 or 0xA0 or 0xA1 => 0x0004,
        0x5B or 0x5C => 0x0008,
        _ => 0
    };

    private static int GetLiveModifiers()
    {
        int mods = 0;
        if ((InfraWindowNative.GetAsyncKeyState(0x11) & 0x8000) != 0)
            mods |= 0x0002;
        if ((InfraWindowNative.GetAsyncKeyState(0x12) & 0x8000) != 0)
            mods |= 0x0001;
        if ((InfraWindowNative.GetAsyncKeyState(0x10) & 0x8000) != 0)
            mods |= 0x0004;
        if ((InfraWindowNative.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
            (InfraWindowNative.GetAsyncKeyState(0x5C) & 0x8000) != 0)
            mods |= 0x0008;
        return mods;
    }
}

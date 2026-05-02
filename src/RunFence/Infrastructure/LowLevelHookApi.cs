namespace RunFence.Infrastructure;

public sealed class LowLevelHookApi : ILowLevelHookApi
{
    public IntPtr InstallKeyboardHook(WindowNative.LowLevelKeyboardProc callback) =>
        WindowNative.SetWindowsHookEx(WindowNative.WH_KEYBOARD_LL, callback, WindowNative.GetModuleHandle(null), 0);

    public IntPtr InstallMouseHook(WindowNative.LowLevelMouseProc callback) =>
        WindowNative.SetWindowsHookEx(WindowNative.WH_MOUSE_LL, callback, WindowNative.GetModuleHandle(null), 0);

    public bool Unhook(IntPtr hook) => WindowNative.UnhookWindowsHookEx(hook);

    public IntPtr CallNextHook(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam) =>
        WindowNative.CallNextHookEx(hook, nCode, wParam, lParam);
}

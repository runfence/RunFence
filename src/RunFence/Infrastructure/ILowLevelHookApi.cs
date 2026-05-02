namespace RunFence.Infrastructure;

public interface ILowLevelHookApi
{
    IntPtr InstallKeyboardHook(WindowNative.LowLevelKeyboardProc callback);
    IntPtr InstallMouseHook(WindowNative.LowLevelMouseProc callback);
    bool Unhook(IntPtr hook);
    IntPtr CallNextHook(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
}

using System.Runtime.InteropServices;

namespace RunFence.UI;

public static class MainUiThreadContextNative
{
    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}

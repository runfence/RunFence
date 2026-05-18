using RunFence.Infrastructure;

namespace RunFence.Startup.UI;

public class WindowsHelloWindowHandleProvider : IWindowsHelloWindowHandleProvider
{
    public IntPtr GetForegroundWindowHandle()
    {
        return WindowNative.GetForegroundWindow();
    }
}

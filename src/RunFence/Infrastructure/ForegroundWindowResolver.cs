using System.Text;

namespace RunFence.Infrastructure;

public sealed class ForegroundWindowResolver : IForegroundWindowResolver
{
    public ForegroundWindowInfo GetForegroundWindow()
    {
        IntPtr hWnd = WindowNative.GetForegroundWindow();
        WindowNative.GetWindowThreadProcessId(hWnd, out uint processId);

        var className = new StringBuilder(256);
        WindowNative.GetClassName(hWnd, className, className.Capacity);

        return new ForegroundWindowInfo(hWnd, processId, className.ToString());
    }
}

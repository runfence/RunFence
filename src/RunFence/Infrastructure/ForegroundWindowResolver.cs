using System.Text;

namespace RunFence.Infrastructure;

public sealed class ForegroundWindowResolver : IForegroundWindowResolver
{
    public ForegroundWindowInfo GetForegroundWindow() =>
        GetWindowInfo(WindowNative.GetForegroundWindow());

    public ForegroundWindowInfo GetWindowInfo(IntPtr hwnd)
    {
        WindowNative.GetWindowThreadProcessId(hwnd, out uint processId);

        var className = new StringBuilder(256);
        WindowNative.GetClassName(hwnd, className, className.Capacity);

        return new ForegroundWindowInfo(hwnd, processId, className.ToString());
    }
}

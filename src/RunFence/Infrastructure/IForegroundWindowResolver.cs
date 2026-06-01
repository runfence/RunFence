namespace RunFence.Infrastructure;

public interface IForegroundWindowResolver
{
    ForegroundWindowInfo GetForegroundWindow();
    ForegroundWindowInfo GetWindowInfo(IntPtr hwnd);
}

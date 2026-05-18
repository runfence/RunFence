namespace RunFence.Security;

public interface IWindowsHelloNative
{
    bool IsSupported();
    Task<WindowsHelloNativeResult> RequestVerification(string message, IntPtr hwnd);
    WindowsHelloNativeResult RequestVerificationBlocking(string message, IntPtr hwnd);
}

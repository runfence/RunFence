namespace RunFence.Security;

public class WindowsHelloNative : IWindowsHelloNative
{
    public bool IsSupported() => WindowsHelloInterop.IsSystemAvailable();

    public async Task<WindowsHelloNativeResult> RequestVerification(string message, IntPtr hwnd)
        => await RequestVerificationCoreAsync(() => WindowsHelloInterop.RequestAsync(hwnd, message));

    public WindowsHelloNativeResult RequestVerificationBlocking(string message, IntPtr hwnd)
        => RequestVerificationCore(() => WindowsHelloInterop.RequestBlocking(hwnd, message));

    private async Task<WindowsHelloNativeResult> RequestVerificationCoreAsync(Func<Task<HelloVerificationResult>> request)
    {
        try
        {
            return Map(await request());
        }
        catch (Exception ex)
        {
            return CreateFailure(ex);
        }
    }

    private WindowsHelloNativeResult RequestVerificationCore(Func<HelloVerificationResult> request)
    {
        try
        {
            return Map(request());
        }
        catch (Exception ex)
        {
            return CreateFailure(ex);
        }
    }

    private static WindowsHelloNativeResult Map(HelloVerificationResult result) => result switch
    {
        HelloVerificationResult.Verified => new WindowsHelloNativeResult(WindowsHelloNativeStatus.Verified, null, null),
        HelloVerificationResult.Canceled => new WindowsHelloNativeResult(WindowsHelloNativeStatus.Canceled, null, "Verification canceled."),
        HelloVerificationResult.NotAvailable => new WindowsHelloNativeResult(WindowsHelloNativeStatus.Unavailable, null, "Windows Hello is unavailable."),
        _ => new WindowsHelloNativeResult(WindowsHelloNativeStatus.Failed, null, "Windows Hello verification failed.")
    };

    private static WindowsHelloNativeResult CreateFailure(Exception ex) =>
        new(
            WindowsHelloNativeStatus.Failed,
            ex.HResult,
            ex.Message);
}

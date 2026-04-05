using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Security;

/// <summary>
/// Windows Hello verification via direct WinRT COM interop.
/// Tries the current (elevated admin) account first, then falls back to the interactive user if different.
/// </summary>
public class WindowsHelloService : IWindowsHelloService
{
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    private readonly ILoggingService _log;

    public WindowsHelloService(ILoggingService log)
    {
        _log = log;
    }

    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(WindowsHelloNative.IsSystemAvailable());
    }

    public HelloVerificationResult VerifySync(string message)
    {
        // Try current (elevated) account first
        var result = VerifyAccountSync(message, accountLabel: "current account");

        if (result is HelloVerificationResult.Verified or HelloVerificationResult.Canceled)
            return result;

        // Current account failed - try interactive user if different
        var interactiveUserSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveUserSid != null && !SidResolutionHelper.IsCurrentUserInteractive())
        {
            var reason = result == HelloVerificationResult.NotAvailable ? "not available" : "failed";
            _log.Warn($"Windows Hello {reason} for current account, trying interactive user");
            var interactiveResult = TryVerifyInteractiveUserSync(message);

            switch (interactiveResult)
            {
                case HelloVerificationResult.Verified:
                    _log.Info("Windows Hello verification succeeded for interactive user");
                    return interactiveResult;
                case HelloVerificationResult.Canceled:
                    _log.Info("Windows Hello verification canceled for interactive user");
                    return interactiveResult;
                default:
                    _log.Warn("Windows Hello not available for interactive user either: " + interactiveResult);
                    break;
            }
        }

        return result;
    }

    private HelloVerificationResult TryVerifyInteractiveUserSync(string message)
    {
        try
        {
            IntPtr hToken = IntPtr.Zero;
            try
            {
                hToken = ExplorerTokenHelper.TryGetExplorerToken(_log);
                if (hToken == IntPtr.Zero)
                {
                    _log.Warn("Could not obtain explorer token for interactive user");
                    return HelloVerificationResult.NotAvailable;
                }

                if (!ImpersonateLoggedOnUser(hToken))
                {
                    _log.Warn("Failed to impersonate interactive user");
                    return HelloVerificationResult.NotAvailable;
                }

                try
                {
                    return VerifyAccountSync(message, accountLabel: "interactive user");
                }
                finally
                {
                    if (!RevertToSelf())
                    {
                        _log.Error("Failed to revert impersonation after Windows Hello verification");
                    }
                }
            }
            finally
            {
                if (hToken != IntPtr.Zero)
                    NativeMethods.CloseHandle(hToken);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Windows Hello verification for interactive user failed: {ex.Message}");
            return HelloVerificationResult.NotAvailable;
        }
    }

    private HelloVerificationResult VerifyAccountSync(string message, string accountLabel)
    {
        // Get HWND on calling thread before entering Task.Run
        var hwnd = NativeInterop.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            _log.Warn($"Windows Hello not available for {accountLabel}: no foreground window handle");
            return HelloVerificationResult.NotAvailable;
        }

        try
        {
            var nativeResult = Task.Run(() => WindowsHelloNative.RequestAsync(hwnd, message))
                .GetAwaiter().GetResult();

            switch (nativeResult)
            {
                case WinHelloVerificationResult.Verified:
                    _log.Info($"Windows Hello verification succeeded for {accountLabel}");
                    return HelloVerificationResult.Verified;
                case WinHelloVerificationResult.Canceled:
                    return HelloVerificationResult.Canceled;
                case WinHelloVerificationResult.DeviceNotPresent:
                    _log.Warn($"Windows Hello not available for {accountLabel}: device not present");
                    return HelloVerificationResult.NotAvailable;
                case WinHelloVerificationResult.NotConfiguredForUser:
                    _log.Warn($"Windows Hello not available for {accountLabel}: not configured for user");
                    return HelloVerificationResult.NotAvailable;
                case WinHelloVerificationResult.DisabledByPolicy:
                    _log.Warn($"Windows Hello not available for {accountLabel}: disabled by policy");
                    return HelloVerificationResult.NotAvailable;
                case WinHelloVerificationResult.DeviceBusy:
                    _log.Warn($"Windows Hello not available for {accountLabel}: device busy");
                    return HelloVerificationResult.NotAvailable;
                case WinHelloVerificationResult.RetriesExhausted:
                    _log.Warn($"Windows Hello not available for {accountLabel}: retries exhausted");
                    return HelloVerificationResult.NotAvailable;
                default:
                    _log.Error($"Windows Hello verification failed for {accountLabel}: unknown result {nativeResult}");
                    return HelloVerificationResult.Failed;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Windows Hello verification failed for {accountLabel}: {ex.Message}", ex);
            return HelloVerificationResult.Failed;
        }
    }
}
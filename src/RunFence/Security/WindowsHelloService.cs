using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Security;

/// <summary>
/// Windows Hello verification via direct WinRT COM interop.
/// Tries the current (elevated admin) account first, then falls back to the interactive user if different.
/// </summary>
public class WindowsHelloService(
    ILoggingService log,
    IWindowsHelloExecutionContext executionContext,
    IWindowsHelloNative windowsHelloNative) : IWindowsHelloService
{
    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(windowsHelloNative.IsSupported());
    }

    public async Task<HelloVerificationResult> VerifyAsync(string message)
    {
        // Try current (elevated) account first
        var result = await VerifyAccountAsync(message, accountLabel: "current account");

        if (result is HelloVerificationResult.Verified or HelloVerificationResult.Canceled)
            return result;

        // Current account failed - try interactive user if different
        var interactiveUserSid = executionContext.GetInteractiveUserSid();
        if (interactiveUserSid != null && !executionContext.IsCurrentUserInteractive())
        {
            var reason = result == HelloVerificationResult.NotAvailable ? "not available" : "failed";
            log.Warn($"Windows Hello {reason} for current account, trying interactive user");
            var interactiveResult = await TryVerifyInteractiveUserAsync(message);

            switch (interactiveResult)
            {
                case HelloVerificationResult.Verified:
                    log.Info("Windows Hello verification succeeded for interactive user");
                    return interactiveResult;
                case HelloVerificationResult.Canceled:
                    log.Info("Windows Hello verification canceled for interactive user");
                    return interactiveResult;
                default:
                    log.Warn("Windows Hello not available for interactive user either: " + interactiveResult);
                    break;
            }
        }

        return result;
    }

    private async Task<HelloVerificationResult> TryVerifyInteractiveUserAsync(string message)
    {
        // Get HWND on calling (UI) thread before entering Task.Run
        var hwnd = executionContext.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            log.Warn("Windows Hello not available for interactive user: no foreground window handle");
            return HelloVerificationResult.NotAvailable;
        }

        try
        {
            IntPtr hToken = executionContext.TryGetExplorerToken();
            if (hToken == IntPtr.Zero)
            {
                log.Warn("Could not obtain explorer token for interactive user");
                return HelloVerificationResult.NotAvailable;
            }

            try
            {
                // Perform impersonation, WinRT verification, and revert all on the same
                // dedicated thread so impersonation never crosses thread boundaries.
                return await Task.Run(() =>
                {
                    if (!executionContext.ImpersonateLoggedOnUser(hToken))
                    {
                        log.Warn("Failed to impersonate interactive user");
                        return HelloVerificationResult.NotAvailable;
                    }

                    try
                    {
                        var nativeResult = windowsHelloNative.RequestVerificationBlocking(message, hwnd);
                        var result = Map(nativeResult);

                        switch (result)
                        {
                            case HelloVerificationResult.NotAvailable:
                                log.Warn("Windows Hello not available for interactive user");
                                break;
                            case HelloVerificationResult.Failed:
                                log.Error("Windows Hello verification failed for interactive user");
                                break;
                        }

                        return result;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Windows Hello verification failed for interactive user: {ex.Message}", ex);
                        return HelloVerificationResult.Failed;
                    }
                    finally
                    {
                        if (!executionContext.RevertToSelf())
                            log.Error("Failed to revert impersonation after Windows Hello verification");
                    }
                });
            }
            finally
            {
                executionContext.CloseHandle(hToken);
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Windows Hello verification for interactive user failed: {ex.Message}");
            return HelloVerificationResult.NotAvailable;
        }
    }

    private async Task<HelloVerificationResult> VerifyAccountAsync(string message, string accountLabel)
    {
        // Get HWND on calling thread before entering Task.Run
        var hwnd = executionContext.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            log.Warn($"Windows Hello not available for {accountLabel}: no foreground window handle");
            return HelloVerificationResult.NotAvailable;
        }

        try
        {
            var nativeResult = await Task.Run(() => windowsHelloNative.RequestVerification(message, hwnd));
            var result = Map(nativeResult);

            switch (result)
            {
                case HelloVerificationResult.Verified:
                    log.Info($"Windows Hello verification succeeded for {accountLabel}");
                    return result;
                case HelloVerificationResult.Canceled:
                    return result;
                case HelloVerificationResult.NotAvailable:
                    log.Warn($"Windows Hello not available for {accountLabel}");
                    return result;
                default:
                    log.Error($"Windows Hello verification failed for {accountLabel}: result {result}");
                    return result;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Windows Hello verification failed for {accountLabel}: {ex.Message}", ex);
            return HelloVerificationResult.Failed;
        }
    }

    private static HelloVerificationResult Map(WindowsHelloNativeResult result)
    {
        return result.Status switch
        {
            WindowsHelloNativeStatus.Verified => HelloVerificationResult.Verified,
            WindowsHelloNativeStatus.Canceled => HelloVerificationResult.Canceled,
            WindowsHelloNativeStatus.Unavailable => HelloVerificationResult.NotAvailable,
            _ => HelloVerificationResult.Failed
        };
    }
}

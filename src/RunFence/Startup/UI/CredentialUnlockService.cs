using RunFence.Security;

namespace RunFence.Startup.UI;

public class CredentialUnlockService(
    IUnlockPinPrompt unlockPinPrompt,
    IWindowsHelloNative windowsHelloNative,
    IWindowsHelloPinFallbackPrompt windowsHelloPinFallbackPrompt,
    IWindowsHelloWindowHandleProvider windowsHelloWindowHandleProvider)
    : ICredentialUnlockService
{
    public CredentialUnlockResult VerifyPin() => unlockPinPrompt.TryVerify()
        ? CredentialUnlockResult.Succeeded
        : CredentialUnlockResult.Canceled;

    public async Task<CredentialUnlockResult> VerifyAsync(CredentialUnlockMode mode)
    {
        if (mode == CredentialUnlockMode.Pin)
            return VerifyPin();
        if (mode != CredentialUnlockMode.WindowsHelloThenPin)
            return CredentialUnlockResult.Failed;

        var hwnd = windowsHelloWindowHandleProvider.GetForegroundWindowHandle();
        var hello = await windowsHelloNative.RequestVerification("Verify your identity to unlock RunFence", hwnd)
            .ConfigureAwait(false);

        if (hello.Status == WindowsHelloNativeStatus.Verified)
            return CredentialUnlockResult.Succeeded;

        if (hello.Status == WindowsHelloNativeStatus.Canceled)
            return CredentialUnlockResult.Canceled;

        if (hello.Status is WindowsHelloNativeStatus.Unavailable or WindowsHelloNativeStatus.Failed)
        {
            if (!windowsHelloPinFallbackPrompt.ConfirmFallbackToPin(hello))
                return CredentialUnlockResult.Canceled;
        }
        else
        {
            return CredentialUnlockResult.Failed;
        }

        return VerifyPin();
    }
}

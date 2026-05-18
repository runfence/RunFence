using RunFence.Security;
using RunFence.Infrastructure;

namespace RunFence.Startup.UI;

public class WindowsHelloPinFallbackPrompt : IWindowsHelloPinFallbackPrompt, IWindowsHelloPinFallbackPromptEventSource
{
    public event Func<bool>? WindowsHelloUnavailableConfirmRequested;
    public event Func<bool>? WindowsHelloFailedConfirmRequested;

    public bool ConfirmFallbackToPin(WindowsHelloNativeResult result) => result.Status switch
    {
        WindowsHelloNativeStatus.Unavailable => WindowsHelloUnavailableConfirmRequested?.Invoke() ?? false,
        WindowsHelloNativeStatus.Failed => WindowsHelloFailedConfirmRequested?.Invoke() ?? false,
        _ => false
    };
}

using RunFence.Security;

namespace RunFence.Startup.UI;

public interface IWindowsHelloPinFallbackPrompt
{
    bool ConfirmFallbackToPin(WindowsHelloNativeResult result);
}

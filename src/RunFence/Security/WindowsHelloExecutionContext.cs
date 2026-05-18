using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Security;

public class WindowsHelloExecutionContext(IExplorerTokenProvider explorerTokenProvider) : IWindowsHelloExecutionContext
{
    public IntPtr GetForegroundWindow() => WindowNative.GetForegroundWindow();

    public string? GetInteractiveUserSid() => SidResolutionHelper.GetInteractiveUserSid();

    public bool IsCurrentUserInteractive() => SidResolutionHelper.IsCurrentUserInteractive();

    public IntPtr TryGetExplorerToken() => explorerTokenProvider.TryGetExplorerToken();

    public bool ImpersonateLoggedOnUser(IntPtr token) => ProcessNative.ImpersonateLoggedOnUser(token);

    public bool RevertToSelf() => ProcessNative.RevertToSelf();

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);
}

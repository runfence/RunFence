namespace RunFence.Security;

public interface IWindowsHelloExecutionContext
{
    IntPtr GetForegroundWindow();
    string? GetInteractiveUserSid();
    bool IsCurrentUserInteractive();
    IntPtr TryGetExplorerToken();
    bool ImpersonateLoggedOnUser(IntPtr token);
    bool RevertToSelf();
    void CloseHandle(IntPtr handle);
}

namespace RunFence.Account;

public interface IProcessSnapshotSource
{
    IReadOnlyList<int> GetProcessIds();
    string? GetTokenSid(int pid, int tokenInfoClass);
    string? GetAppContainerSid(int pid);
    bool HasExited(int pid);
    ProcessInfo? ReadProcessInfo(int pid);
}

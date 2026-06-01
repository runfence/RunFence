using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

public interface IPrefTransProcessWaiter
{
    SettingsTransferResult WaitForResult(ProcessInfo process, int timeoutMs, string logFilePath, Action? pollCallback);
}

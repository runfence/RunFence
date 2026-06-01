using RunFence.Core;
using RunFence.Launch.Tokens;

namespace RunFence.PrefTrans;

internal sealed class PrefTransProcessWaiter(
    ILoggingService log,
    IPrefTransLogWorkspace logWorkspace,
    IPrefTransProcessHandleFactory processHandleFactory,
    IPrefTransTimeoutClockFactory timeoutClockFactory)
    : IPrefTransProcessWaiter
{
    public SettingsTransferResult WaitForResult(ProcessInfo process, int timeoutMs, string logFilePath, Action? pollCallback)
    {
        using var processHandle = processHandleFactory.Create(process);
        var timeoutClock = timeoutClockFactory.StartNew();

        while (!processHandle.WaitForExit(500))
        {
            if (timeoutClock.ElapsedMilliseconds >= timeoutMs)
            {
                processHandle.Kill(1);
                processHandle.WaitForExit(2000);
                log.Error("Operation timed out");
                return new SettingsTransferResult(false, "Operation timed out");
            }

            pollCallback?.Invoke();
        }

        if (processHandle.ExitCode == 0)
        {
            log.Info("Operation succeeded");
            return new SettingsTransferResult(true, "");
        }

        log.Error($"Operation failed: exit code {processHandle.ExitCode}");
        var logContent = logWorkspace.ReadLogFile(logFilePath);
        if (!string.IsNullOrEmpty(logContent))
            return new SettingsTransferResult(false, logContent);
        return new SettingsTransferResult(false, "Error code: " + processHandle.ExitCode);
    }
}

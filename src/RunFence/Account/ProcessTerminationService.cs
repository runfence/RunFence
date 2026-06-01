using System.ComponentModel;
using System.Diagnostics;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launching.Processes;

namespace RunFence.Account;

public class ProcessTerminationService(
    ILoggingService log,
    IProcessSnapshotEnumerator processEnumerator) : IProcessTerminationService
{
    private static readonly TimeSpan ForceKillWaitTimeout = TimeSpan.FromSeconds(5);

    public ProcessActionResult CloseProcess(int pid, long? expectedStartTimeUtcTicks, string expectedOwnerSid) =>
        ExecuteSingleProcessAction(pid, expectedStartTimeUtcTicks, expectedOwnerSid, p => p.CloseMainWindow(), "close", "Process has no main window.");

    public ProcessActionResult KillProcess(int pid, long? expectedStartTimeUtcTicks, string expectedOwnerSid) =>
        ExecuteSingleProcessAction(pid, expectedStartTimeUtcTicks, expectedOwnerSid, p =>
        {
            p.Kill();
            p.WaitForExit((int)ForceKillWaitTimeout.TotalMilliseconds);
            return p.HasExited;
        }, "kill", null);

    public ProcessKillResult KillProcesses(string sid)
    {
        int tokenInfoClass = ProcessNative.GetTokenInfoClass(sid);

        // Shared deadline: both the original and any newly spawned processes get time within this window.
        var gracefulDeadline = DateTime.UtcNow.Add(ForceKillWaitTimeout);

        // Pass 1: collect matching processes.
        var matching = GetMatchingProcesses(sid, tokenInfoClass);

        // Pass 2: send graceful close to all original processes; track which ones received it.
        var closeWindowSent = new HashSet<int>();
        foreach (var proc in matching)
        {
            try
            {
                if (proc.CloseMainWindow())
                    closeWindowSent.Add(proc.Id);
            }
            catch
            {
                /* no main window or inaccessible */
            }
        }

        // Pass 3: wait for graceful exits within the shared deadline.
        WaitForExitByDeadline(matching, gracefulDeadline);

        // Count processes that received CloseMainWindow and then exited within the timeout.
        // Note: this count may include processes that exited for reasons other than the close
        // message (e.g. they were already in the process of terminating). The count is used
        // only for informational logging, so a slight over-count is acceptable.
        int gracefullyExited = matching.Count(p =>
        {
            if (!closeWindowSent.Contains(p.Id))
                return false;
            try
            {
                return p.HasExited;
            }
            catch
            {
                return false;
            }
        });
        foreach (var proc in matching)
            proc.Dispose();

        // Pass 4: re-scan, catching survivors and any newly spawned processes.
        var rescanned = GetMatchingProcesses(sid, tokenInfoClass);

        // Give newly found processes the remaining grace time from the shared deadline.
        foreach (var proc in rescanned)
        {
            try
            {
                proc.CloseMainWindow();
            }
            catch
            {
            }
        }

        WaitForExitByDeadline(rescanned, gracefulDeadline);

        // Force-kill anything still running, then wait long enough for each forced kill to finish.
        int forceKilled = 0, failed = 0;
        var forceKilledProcesses = new List<Process>();
        foreach (var proc in rescanned)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                    forceKilledProcesses.Add(proc);
                }
            }
            catch (InvalidOperationException)
            {
            } // exited between check and kill; treat as graceful
            catch
            {
                failed++;
            }
        }

        foreach (var proc in forceKilledProcesses)
        {
            try
            {
                proc.WaitForExit((int)ForceKillWaitTimeout.TotalMilliseconds);
            }
            catch
            {
            }
        }

        foreach (var proc in forceKilledProcesses)
        {
            try
            {
                if (proc.HasExited)
                    forceKilled++;
                else
                    failed++;
            }
            catch
            {
                failed++;
            }
        }

        foreach (var proc in rescanned)
            proc.Dispose();

        int killed = gracefullyExited + forceKilled;
        log.Info($"KillProcesses SID={sid}: killed={killed} (graceful={gracefullyExited}, force={forceKilled}), failed={failed}");
        return new ProcessKillResult(killed, failed);
    }

    private ProcessActionResult ExecuteSingleProcessAction(
        int pid,
        long? expectedStartTimeUtcTicks,
        string expectedOwnerSid,
        Func<Process, bool> action,
        string actionName,
        string? noEffectMessage)
    {
        try
        {
            using var live = Process.GetProcessById(pid);
            if (!IsMatchingIdentity(live.Id, expectedStartTimeUtcTicks, expectedOwnerSid))
                return ProcessActionResult.Stale();

            if (!action(live))
                return ProcessActionResult.Failure(noEffectMessage ?? $"Failed to {actionName} process.");
            return ProcessActionResult.Success();
        }
        catch (ArgumentException)
        {
            return ProcessActionResult.Stale();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return ProcessActionResult.Denied(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ProcessActionResult.Denied(ex.Message);
        }
        catch (InvalidOperationException)
        {
            return ProcessActionResult.Stale();
        }
        catch (Exception ex)
        {
            log.Warn($"ProcessTerminationService: failed to {actionName} pid {pid}: {ex.Message}");
            return ProcessActionResult.Failure(ex.Message);
        }
    }

    private static bool IsMatchingIdentity(int pid, long? expectedStartTimeUtcTicks, string expectedOwnerSid)
    {
        IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, pid);
        if (hProcess == IntPtr.Zero)
            return false;

        try
        {
            int tokenInfoClass = ProcessNative.GetTokenInfoClass(expectedOwnerSid);
            var tokenSid = ProcessNative.GetTokenSid(hProcess, tokenInfoClass);
            if (!string.Equals(tokenSid, expectedOwnerSid, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!expectedStartTimeUtcTicks.HasValue)
                return true;

            if (!ProcessNative.GetProcessTimes(hProcess, out var creation, out _, out _, out _))
                return false;

            return DateTime.FromFileTimeUtc(creation.ToLong()).Ticks == expectedStartTimeUtcTicks.Value;
        }
        finally
        {
            ProcessNative.CloseHandle(hProcess);
        }
    }

    private List<Process> GetMatchingProcesses(string sid, int tokenInfoClass)
    {
        int currentPid = Environment.ProcessId;
        var result = new List<Process>();
        foreach (var process in processEnumerator.GetProcesses())
        {
            if (process.ProcessId <= 4 || process.ProcessId == currentPid)
                continue;

            bool isMatch = false;
            IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, process.ProcessId);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    isMatch = string.Equals(ProcessNative.GetTokenSid(hProcess, tokenInfoClass), sid, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    ProcessNative.CloseHandle(hProcess);
                }
            }

            if (!isMatch)
                continue;

            try
            {
                result.Add(Process.GetProcessById(process.ProcessId));
            }
            catch
            {
            }
        }

        return result;
    }

    private static void WaitForExitByDeadline(List<Process> processes, DateTime deadline)
    {
        foreach (var proc in processes)
        {
            try
            {
                WaitForExitByDeadline(proc, deadline);
            }
            catch
            {
            }
        }
    }

    private static bool WaitForExitByDeadline(Process process, DateTime deadline)
    {
        var remainingMs = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
        if (remainingMs <= 0)
            return process.HasExited;

        process.WaitForExit(remainingMs);
        return process.HasExited;
    }
}

using System.Diagnostics;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class ProcessTerminationService(ILoggingService log) : IProcessTerminationService
{
    public (int Killed, int Failed) KillProcesses(string sid)
    {
        int tokenInfoClass = ProcessNative.GetTokenInfoClass(sid);

        // Shared deadline: both the original and any newly spawned processes get time within this window
        var deadline = DateTime.UtcNow.AddSeconds(5);

        // Pass 1: collect matching processes
        var matching = GetMatchingProcesses(sid, tokenInfoClass);

        // Pass 2: send graceful close to all original processes; track which ones received it
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

        // Pass 3: wait for graceful exits within the shared deadline
        WaitForExitByDeadline(matching, deadline);

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

        // Pass 4: re-scan — catches survivors and any newly spawned processes
        var rescanned = GetMatchingProcesses(sid, tokenInfoClass);

        // Give newly found processes the remaining grace time from the shared deadline
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

        WaitForExitByDeadline(rescanned, deadline);

        // Force-kill anything still running
        int forceKilled = 0, failed = 0;
        foreach (var proc in rescanned)
        {
            using (proc)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                        forceKilled++;
                    }
                }
                catch (InvalidOperationException)
                {
                } // exited between check and kill — treat as graceful
                catch
                {
                    failed++;
                }
            }
        }

        int killed = gracefullyExited + forceKilled;
        log.Info($"KillProcesses SID={sid}: killed={killed} (graceful={gracefullyExited}, force={forceKilled}), failed={failed}");
        return (killed, failed);
    }

    private List<Process> GetMatchingProcesses(string sid, int tokenInfoClass)
    {
        int currentPid = Environment.ProcessId;
        var result = new List<Process>();
        foreach (var proc in Process.GetProcesses())
        {
            if (proc.Id <= 4 || proc.Id == currentPid)
            {
                proc.Dispose();
                continue;
            }

            bool isMatch = false;
            IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, proc.Id);
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

            if (isMatch)
                result.Add(proc);
            else
                proc.Dispose();
        }

        return result;
    }

    private static void WaitForExitByDeadline(List<Process> processes, DateTime deadline)
    {
        foreach (var proc in processes)
        {
            try
            {
                var remainingMs = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (remainingMs > 0)
                    proc.WaitForExit(remainingMs);
            }
            catch
            {
            }
        }
    }
}
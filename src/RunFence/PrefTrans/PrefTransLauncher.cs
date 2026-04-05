using System.ComponentModel;
using System.Diagnostics;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.PrefTrans;

public class PrefTransLauncher(IAppLaunchOrchestrator launchOrchestrator, ILoggingService log) : IPrefTransLauncher
{
    public SettingsTransferResult RunAndWait(string prefTransPath, string command, string filePath,
        LaunchCredentials credentials, int timeoutMs, Action? pollCallback)
    {
        if (credentials.TokenSource is LaunchTokenSource.CurrentProcess or LaunchTokenSource.InteractiveUser)
            return RunDeElevated(prefTransPath, command, filePath, credentials.TokenSource, timeoutMs, pollCallback);

        return RunWithCredentials(prefTransPath, command, filePath, credentials, timeoutMs, pollCallback);
    }

    private SettingsTransferResult RunDeElevated(string prefTransPath, string command, string filePath,
        LaunchTokenSource tokenSource, int timeoutMs, Action? pollCallback)
    {
        var targetSid = tokenSource == LaunchTokenSource.CurrentProcess
            ? SidResolutionHelper.GetCurrentUserSid()
            : SidResolutionHelper.GetInteractiveUserSid();

        if (targetSid == null)
        {
            const string msg = "No user session found for de-elevated operation.";
            log.Error(msg);
            return new SettingsTransferResult(false, msg);
        }

        var sharedTempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Constants.AppName, "temp");
        Directory.CreateDirectory(sharedTempDir);
        var logFilePath = Path.Combine(sharedTempDir, $"rfn_preftrans_{Guid.NewGuid():N}.log");

        try
        {
            int pid;
            try
            {
                pid = launchOrchestrator.LaunchExeReturnPid(prefTransPath, targetSid,
                    [command, filePath, "--logfile", logFilePath], hideWindow: true);
            }
            catch (Exception ex)
            {
                log.Error("De-elevated operation failed to launch", ex);
                return new SettingsTransferResult(false, $"Operation failed: {ex.Message}");
            }

            if (pid <= 0)
            {
                log.Error("De-elevated operation: failed to obtain process ID");
                return new SettingsTransferResult(false, "Failed to start de-elevated process.");
            }

            var hProcess = ProcessLaunchNative.OpenProcess(
                ProcessLaunchNative.SYNCHRONIZE | ProcessLaunchNative.PROCESS_QUERY_INFORMATION,
                false, (uint)pid);

            if (hProcess == IntPtr.Zero)
            {
                // Process already exited before we could open a handle — check log for errors
                var earlyLog = ReadLogFile(logFilePath);
                if (!string.IsNullOrEmpty(earlyLog))
                    return new SettingsTransferResult(false, $"Operation failed: {earlyLog}");
                log.Info("De-elevated operation completed before handle obtained");
                return new SettingsTransferResult(true, "Operation completed successfully.");
            }

            try
            {
                var sw = Stopwatch.StartNew();
                while (ProcessLaunchNative.WaitForSingleObject(hProcess, 500) != ProcessLaunchNative.WAIT_OBJECT_0)
                {
                    if (sw.ElapsedMilliseconds > timeoutMs)
                    {
                        ProcessLaunchNative.TerminateProcess(hProcess, 1);
                        ProcessLaunchNative.WaitForSingleObject(hProcess, 2000);
                        log.Error("De-elevated operation timed out");
                        return new SettingsTransferResult(false, "Operation timed out waiting for completion.");
                    }
                    pollCallback?.Invoke();
                }

                ProcessLaunchNative.GetExitCodeProcess(hProcess, out var exitCode);

                if (exitCode == 0)
                {
                    log.Info("De-elevated operation succeeded");
                    return new SettingsTransferResult(true, "Operation completed successfully.");
                }

                log.Error($"De-elevated operation failed: exit code {exitCode}");
                var logContent = ReadLogFile(logFilePath);
                if (!string.IsNullOrEmpty(logContent))
                    return new SettingsTransferResult(false, $"Operation failed: {logContent}");
                return new SettingsTransferResult(false, $"Operation failed with exit code {exitCode}.");
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        finally
        {
            try { File.Delete(logFilePath); } catch { }
        }
    }

    private SettingsTransferResult RunWithCredentials(string prefTransPath, string command, string filePath,
        LaunchCredentials credentials, int timeoutMs, Action? pollCallback)
    {
        var displayName = string.IsNullOrEmpty(credentials.Domain)
            ? credentials.Username
            : $"{credentials.Domain}\\{credentials.Username}";

        var psi = new ProcessStartInfo
        {
            FileName = prefTransPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        ProcessStartInfoHelper.SetCredentials(psi, credentials.Username, credentials.Domain, credentials.Password!);
        psi.ArgumentList.Add(command);
        psi.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                const string startFailMsg = "Failed to start preftrans process.";
                log.Error(startFailMsg);
                return new SettingsTransferResult(false, startFailMsg);
            }

            // Start async stderr read before entering the polling loop to avoid deadlock:
            // if the process fills its stderr buffer while we are blocking in Thread.Sleep,
            // the process hangs waiting for stderr to be drained, and we never exit the loop.
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

            var sw = Stopwatch.StartNew();
            while (!process.HasExited)
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    } // best-effort kill

                    try
                    {
                        process.WaitForExit(2000);
                    }
                    catch
                    {
                    } // best-effort wait

                    log.Error($"Settings import timed out for {displayName}");
                    return new SettingsTransferResult(false, "Import timed out.");
                }

                pollCallback?.Invoke();
                Thread.Sleep(500);
            }

            var stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode == 0)
            {
                log.Info($"Settings imported for {displayName}");
                return new SettingsTransferResult(true, "Settings imported successfully.");
            }

            var failMsg = string.IsNullOrWhiteSpace(stderr)
                ? $"Import failed with exit code {process.ExitCode}."
                : $"Import failed: {stderr.Trim()}";
            log.Error($"Settings import failed for {displayName}: exit code {process.ExitCode}, stderr: {stderr}");
            return new SettingsTransferResult(false, failMsg);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error($"Settings import credential failure for {displayName}", ex);
            return new SettingsTransferResult(false, "Stored credentials are incorrect.");
        }
        catch (Exception ex)
        {
            log.Error($"Settings import failed for {displayName}", ex);
            return new SettingsTransferResult(false, $"Import failed: {ex.Message}");
        }
    }

    private string ReadLogFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty; }
        catch { return string.Empty; }
    }
}

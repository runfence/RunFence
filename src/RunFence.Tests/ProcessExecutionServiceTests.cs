using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ProcessExecutionServiceTests
{
    private static readonly TimeSpan ProcessStartupTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Run_StartFailure_ReturnsNotStartedResult()
    {
        var service = new ProcessExecutionService();

        var result = service.Run(new ProcessExecutionRequest(
            FileName: "RunFence_Missing_Process.exe",
            Arguments: string.Empty,
            Timeout: TimeSpan.FromSeconds(5),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None));

        Assert.False(result.Started);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public void Run_CapturesStandardOutputAndStandardError()
    {
        using var tempDir = new TempDirectory("RunFence_ProcessExecution");
        var scriptPath = CreateBatchFile(tempDir.Path, """
            @echo off
            echo stdout line
            echo stderr line 1>&2
            exit /b 7
            """);
        var service = new ProcessExecutionService();

        var result = service.Run(new ProcessExecutionRequest(
            FileName: "cmd.exe",
            Arguments: $"/d /c \"\"{scriptPath}\"\"",
            Timeout: TimeSpan.FromSeconds(5),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None));

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("stdout line", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stderr line", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_Timeout_KillsProcessAndReturnsTimedOutResult()
    {
        using var tempDir = new TempDirectory("RunFence_ProcessExecution");
        var startedPath = Path.Combine(tempDir.Path, "started.txt");
        var pidPath = Path.Combine(tempDir.Path, "pid.txt");
        var service = new ProcessExecutionService();

        var runTask = Task.Run(() => service.Run(new ProcessExecutionRequest(
            FileName: "powershell.exe",
            Arguments: BuildReadinessFirstPowerShellArguments(startedPath, pidPath),
            Timeout: ProcessStartupTimeout + TimeSpan.FromSeconds(1),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None)));

        WaitForFile(startedPath, ProcessStartupTimeout);
        WaitForFile(pidPath, ProcessStartupTimeout);
        var pid = int.Parse(ReadAllTextWhenAvailable(pidPath, ProcessStartupTimeout).Trim(), CultureInfo.InvariantCulture);

        var result = await runTask;

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.True(WaitForProcessExit(pid, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task RunAsync_CancellationAfterStart_KillsProcessAndRethrows()
    {
        using var tempDir = new TempDirectory("RunFence_ProcessExecution");
        var startedPath = Path.Combine(tempDir.Path, "started.txt");
        var pidPath = Path.Combine(tempDir.Path, "pid.txt");
        var service = new ProcessExecutionService();
        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(new ProcessExecutionRequest(
            FileName: "powershell.exe",
            Arguments: BuildReadinessFirstPowerShellArguments(startedPath, pidPath),
            Timeout: TimeSpan.FromSeconds(3),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: cts.Token));

        WaitForFile(startedPath, ProcessStartupTimeout);
        WaitForFile(pidPath, ProcessStartupTimeout);
        var pid = int.Parse(ReadAllTextWhenAvailable(pidPath, ProcessStartupTimeout).Trim(), CultureInfo.InvariantCulture);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
        Assert.True(WaitForProcessExit(pid, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Run_CancellationAfterStart_KillsProcessAndRethrows()
    {
        using var tempDir = new TempDirectory("RunFence_ProcessExecution");
        var startedPath = Path.Combine(tempDir.Path, "started.txt");
        var pidPath = Path.Combine(tempDir.Path, "pid.txt");
        var service = new ProcessExecutionService();
        using var cts = new CancellationTokenSource();

        var runTask = Task.Run(() => service.Run(new ProcessExecutionRequest(
                FileName: "powershell.exe",
                Arguments: BuildReadinessFirstPowerShellArguments(startedPath, pidPath),
                Timeout: TimeSpan.FromSeconds(3),
                KillEntireProcessTreeOnTimeout: true,
                RedirectStandardOutput: true,
                RedirectStandardError: true,
                CancellationToken: cts.Token)));

        WaitForFile(startedPath, ProcessStartupTimeout);
        WaitForFile(pidPath, ProcessStartupTimeout);
        var pid = int.Parse(ReadAllTextWhenAvailable(pidPath, ProcessStartupTimeout).Trim(), CultureInfo.InvariantCulture);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await runTask);
        Assert.True(WaitForProcessExit(pid, TimeSpan.FromSeconds(3)));
    }

    private static string CreateBatchFile(string directoryPath, string contents)
    {
        var scriptPath = Path.Combine(directoryPath, $"{Guid.NewGuid():N}.cmd");
        File.WriteAllText(scriptPath, contents.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return scriptPath;
    }

    private static string BuildReadinessFirstPowerShellArguments(string startedPath, string pidPath)
    {
        var escapedStartedPath = startedPath.Replace("'", "''", StringComparison.Ordinal);
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            $startedStream = [System.IO.File]::Open('{{escapedStartedPath}}', [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
            try {
                $startedWriter = [System.IO.StreamWriter]::new($startedStream, [System.Text.UTF8Encoding]::new($false))
                try {
                    $startedWriter.Write('started')
                    $startedWriter.Flush()
                    $startedStream.Flush($true)
                }
                finally {
                    $startedWriter.Dispose()
                }
            }
            finally {
                $startedStream.Dispose()
            }

            $pidStream = [System.IO.File]::Open('{{escapedPidPath}}', [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
            try {
                $pidWriter = [System.IO.StreamWriter]::new($pidStream, [System.Text.UTF8Encoding]::new($false))
                try {
                    $pidWriter.Write([string]$PID)
                    $pidWriter.Flush()
                    $pidStream.Flush($true)
                }
                finally {
                    $pidWriter.Dispose()
                }
            }
            finally {
                $pidStream.Dispose()
            }

            $gate = [System.Threading.ManualResetEventSlim]::new($false)
            $gate.Wait()
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -NonInteractive -EncodedCommand {encodedScript}";
    }

    private static void WaitForFile(string path, TimeSpan timeout)
    {
        if (SpinWait.SpinUntil(() => File.Exists(path), timeout))
            return;

        throw new TimeoutException($"Timed out waiting for file '{path}'.");
    }

    private static string ReadAllTextWhenAvailable(string path, TimeSpan timeout)
    {
        string? contents = null;
        if (SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        contents = File.ReadAllText(path);
                        return true;
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                },
                timeout))
        {
            return contents!;
        }

        throw new TimeoutException($"Timed out waiting to read file '{path}'.");
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
        => SpinWait.SpinUntil(
            () =>
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    return process.HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            },
            timeout);
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace RunFence.Infrastructure;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private const int TerminationWaitMilliseconds = 5_000;

    public ProcessExecutionResult Run(ProcessExecutionRequest request)
    {
        var startInfo = CreateStartInfo(request);
        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var outputLock = new object();
        var errorLock = new object();

        if (request.RedirectStandardOutput)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                lock (outputLock)
                    standardOutput.AppendLine(e.Data);
            };
        }

        if (request.RedirectStandardError)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                lock (errorLock)
                    standardError.AppendLine(e.Data);
            };
        }

        try
        {
            if (!process.Start())
            {
                return new ProcessExecutionResult(
                    Started: false,
                    ExitCode: null,
                    TimedOut: false,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    FailureMessage: $"Failed to start process '{request.FileName}'.");
            }
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: ex.Message);
        }

        if (request.RedirectStandardOutput)
            process.BeginOutputReadLine();
        if (request.RedirectStandardError)
            process.BeginErrorReadLine();

        var cancellationTerminatedProcess = 0;
        using var cancellationRegistration = request.CancellationToken.Register(
            () =>
            {
                if (process.HasExited)
                    return;

                Interlocked.Exchange(ref cancellationTerminatedProcess, 1);
                TerminateProcess(process, request.KillEntireProcessTreeOnTimeout);
            });
        var timeoutDeadline = Stopwatch.StartNew();

        while (true)
        {
            var remainingTimeout = request.Timeout - timeoutDeadline.Elapsed;
            if (remainingTimeout <= TimeSpan.Zero)
            {
                TerminateProcess(process, request.KillEntireProcessTreeOnTimeout);
                return new ProcessExecutionResult(
                    Started: true,
                    ExitCode: process.HasExited ? process.ExitCode : null,
                    TimedOut: true,
                    StandardOutput: ReadCapturedOutput(standardOutput, outputLock),
                    StandardError: ReadCapturedOutput(standardError, errorLock),
                    FailureMessage: null);
            }

            var waitMilliseconds = (int)Math.Min(100, remainingTimeout.TotalMilliseconds);
            if (process.WaitForExit(waitMilliseconds))
                break;

            if (request.CancellationToken.IsCancellationRequested)
            {
                WaitForExitAfterTermination(process);
                throw new OperationCanceledException(request.CancellationToken);
            }
        }

        if (Volatile.Read(ref cancellationTerminatedProcess) == 1)
            throw new OperationCanceledException(request.CancellationToken);

        process.WaitForExit();
        return new ProcessExecutionResult(
            Started: true,
            ExitCode: process.ExitCode,
            TimedOut: false,
            StandardOutput: ReadCapturedOutput(standardOutput, outputLock),
            StandardError: ReadCapturedOutput(standardError, errorLock),
            FailureMessage: null);
    }

    public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request)
    {
        var startInfo = CreateStartInfo(request);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: ex.Message);
        }

        if (process == null)
        {
            return new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: $"Failed to start process '{request.FileName}'.");
        }

        using (process)
        {
            var standardOutputTask = request.RedirectStandardOutput
                ? process.StandardOutput.ReadToEndAsync()
                : Task.FromResult(string.Empty);
            var standardErrorTask = request.RedirectStandardError
                ? process.StandardError.ReadToEndAsync()
                : Task.FromResult(string.Empty);

            using var timeoutCts = new CancellationTokenSource(request.Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                request.CancellationToken,
                timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                await TerminateProcessAsync(process, request.KillEntireProcessTreeOnTimeout).ConfigureAwait(false);
                throw new OperationCanceledException(request.CancellationToken);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                var exited = await TerminateProcessAsync(process, request.KillEntireProcessTreeOnTimeout).ConfigureAwait(false);
                return new ProcessExecutionResult(
                    Started: true,
                    ExitCode: process.HasExited ? process.ExitCode : null,
                    TimedOut: true,
                    StandardOutput: exited ? await standardOutputTask.ConfigureAwait(false) : GetCompletedOutputOrEmpty(standardOutputTask),
                    StandardError: exited ? await standardErrorTask.ConfigureAwait(false) : GetCompletedOutputOrEmpty(standardErrorTask),
                    FailureMessage: null);
            }

            return new ProcessExecutionResult(
                Started: true,
                ExitCode: process.ExitCode,
                TimedOut: false,
                StandardOutput: await standardOutputTask.ConfigureAwait(false),
                StandardError: await standardErrorTask.ConfigureAwait(false),
                FailureMessage: null);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request) =>
        new(request.FileName, request.Arguments)
        {
            UseShellExecute = request.UseShellExecute,
            CreateNoWindow = !request.UseShellExecute,
            WindowStyle = request.WindowStyle,
            RedirectStandardOutput = request.RedirectStandardOutput,
            RedirectStandardError = request.RedirectStandardError
        };

    private static string ReadCapturedOutput(StringBuilder builder, object syncLock)
    {
        lock (syncLock)
            return builder.ToString();
    }

    private static bool TerminateProcess(Process process, bool killEntireProcessTree)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: killEntireProcessTree);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }

        return WaitForExitAfterTermination(process);
    }

    private static bool WaitForExitAfterTermination(Process process)
    {
        try
        {
            return process.WaitForExit(TerminationWaitMilliseconds);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task<bool> TerminateProcessAsync(Process process, bool killEntireProcessTree)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: killEntireProcessTree);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }

        try
        {
            using var waitCts = new CancellationTokenSource(TerminationWaitMilliseconds);
            await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static string GetCompletedOutputOrEmpty(Task<string> outputTask) =>
        outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
}

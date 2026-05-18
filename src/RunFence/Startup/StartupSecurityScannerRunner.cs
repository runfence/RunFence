using RunFence.Infrastructure;

namespace RunFence.Startup;

public sealed class StartupSecurityScannerRunner(
    IProcessExecutionService processExecutionService,
    string scannerPath)
{
    private const int ScannerTimeoutMs = 120_000;

    public ProcessExecutionResult Run(CancellationToken cancellationToken)
    {
        if (!File.Exists(scannerPath))
        {
            return new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: $"Security scanner not found: {scannerPath}");
        }

        var result = processExecutionService.Run(new ProcessExecutionRequest(
            FileName: scannerPath,
            Arguments: string.Empty,
            Timeout: TimeSpan.FromMilliseconds(ScannerTimeoutMs),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: cancellationToken));

        return result.Started
            ? result
            : result with
            {
                FailureMessage = string.IsNullOrWhiteSpace(result.FailureMessage)
                    ? "Failed to start security scanner process."
                    : $"Failed to start security scanner process: {result.FailureMessage}"
            };
    }
}

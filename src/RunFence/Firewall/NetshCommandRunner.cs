using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class NetshCommandRunner(IProcessExecutionService processExecutionService) : INetshCommandRunner
{
    public async Task<DynamicPortRangeCommandResult> RunAsync(string arguments)
    {
        var result = await processExecutionService.RunAsync(new ProcessExecutionRequest(
            FileName: "netsh",
            Arguments: arguments,
            Timeout: TimeSpan.FromSeconds(5),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None));

        if (!result.Started)
            throw new InvalidOperationException(result.FailureMessage ?? "Failed to start netsh.");
        if (result.TimedOut)
            return new DynamicPortRangeCommandResult(-1, string.Empty, TimedOut: true, "netsh timed out.");

        return new DynamicPortRangeCommandResult(
            result.ExitCode ?? -1,
            result.StandardOutput,
            TimedOut: false,
            result.ExitCode == 0
                ? null
                : string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"netsh exited with code {result.ExitCode}."
                    : result.StandardError.Trim());
    }
}

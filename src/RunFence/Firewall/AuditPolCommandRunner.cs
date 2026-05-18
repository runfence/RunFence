using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class AuditPolCommandRunner(
    ILoggingService log,
    IProcessExecutionService processExecutionService)
    : IAuditPolCommandRunner
{
    public AuditPolCommandResult Run(string args)
    {
        var result = processExecutionService.Run(new ProcessExecutionRequest(
            FileName: "auditpol.exe",
            Arguments: args,
            Timeout: TimeSpan.FromSeconds(5),
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None));

        if (!result.Started)
            throw new InvalidOperationException(result.FailureMessage ?? "Failed to start auditpol.exe");
        if (result.TimedOut)
            throw new TimeoutException("auditpol.exe timed out");

        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StandardError))
            log.Warn($"auditpol.exe exited with code {result.ExitCode} (args: {args})");
        return new AuditPolCommandResult(result.ExitCode ?? -1, result.StandardOutput, result.StandardError);
    }
}

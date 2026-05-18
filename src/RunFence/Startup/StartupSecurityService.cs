using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup;

public class StartupSecurityService(
    ILoggingService log,
    StartupSecurityScannerRunner scannerRunner)
    : IStartupSecurityService
{
    public List<StartupSecurityFinding> RunChecks(CancellationToken cancellationToken = default)
    {
        var runResult = scannerRunner.Run(cancellationToken);
        if (!runResult.Started)
        {
            log.Error(runResult.FailureMessage ?? "Failed to start security scanner process.");
            return [];
        }

        if (runResult.TimedOut)
            log.Warn("Security scanner timed out.");
        if (runResult.ExitCode is not null and not 0)
            log.Warn($"Security scanner exited with code {runResult.ExitCode}: {runResult.StandardError}");

        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StringReader(runResult.StandardOutput);
        return ParseFindings(reader);
    }

    public static List<StartupSecurityFinding> ParseFindings(TextReader reader)
    {
        var findings = new List<StartupSecurityFinding>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 5)
                continue;
            if (!Enum.TryParse<StartupSecurityCategory>(parts[0], out var category))
                continue;
            var navTarget = parts.Length >= 6 && !string.IsNullOrEmpty(parts[5]) ? parts[5] : null;
            findings.Add(new StartupSecurityFinding(category, parts[1], parts[2], parts[3], parts[4], navTarget));
        }

        return findings;
    }
}

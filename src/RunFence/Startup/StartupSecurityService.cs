using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Startup;

public class StartupSecurityService(ILoggingService log) : IStartupSecurityService
{
    private const int ScannerTimeoutMs = 120_000;

    public List<StartupSecurityFinding> RunChecks(CancellationToken cancellationToken = default)
    {
        var scannerPath = Path.Combine(AppContext.BaseDirectory, Constants.SecurityScannerExeName);
        if (!File.Exists(scannerPath))
        {
            log.Error($"Security scanner not found: {scannerPath}");
            return [];
        }

        var psi = new ProcessStartInfo
        {
            FileName = scannerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            log.Error("Failed to start security scanner process.");
            return [];
        }

        // Register cancellation to kill the process
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch
            {
            }
        });

        string? stderr = null;
        var stderrTask = Task.Run(() => stderr = process.StandardError.ReadToEnd());
        var stdoutTask = Task.Run(() => ParseFindings(process.StandardOutput));

        if (!process.WaitForExit(ScannerTimeoutMs))
        {
            log.Warn("Security scanner timed out.");
            try
            {
                process.Kill();
            }
            catch
            {
            }
        }

        // After exit or kill, streams reach EOF — tasks complete
        stdoutTask.Wait(5000);
        stderrTask.Wait(5000);

        if (process.HasExited && process.ExitCode != 0)
            log.Warn($"Security scanner exited with code {process.ExitCode}: {stderr}");

        cancellationToken.ThrowIfCancellationRequested();
        return stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : [];
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
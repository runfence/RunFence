using System.Diagnostics;
using System.ComponentModel;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RunFence.AppxLauncher;
using RunFence.Core;
using RunFence.Launching.Processes;
using Xunit;

namespace RunFence.IntegrationTests;

public sealed class AppxLauncherProgramMainTests
{
    private static readonly TimeSpan ProcessAppearanceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessSurvivalCheckDelay = TimeSpan.FromSeconds(3);

    public static TheoryData<string, string, int> InstalledAppxApps => new()
    {
        { "OpenAI.Codex", "OpenAI.Codex_2p2nqsd0c76g0", 0 },
        { "Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", (int)AppxLaunchExitCode.TargetProcessVerificationFailed },
        { "Microsoft.WindowsNotepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", 0 }
    };

    [Theory]
    [MemberData(nameof(InstalledAppxApps))]
    public void LaunchParsed_InstalledAppxExecutable_ReturnsExpectedExitCodeAndCleansUpNewProcess(
        string packageName,
        string packageFamilyName,
        int expectedExitCode)
    {
        var package = QueryPackage(packageName, packageFamilyName);
        if (package is null)
            throw Xunit.Sdk.SkipException.ForSkip($"Skipping AppX launcher test because {packageFamilyName} is not installed.");

        var executablePath = ResolvePrimaryExecutablePath(package.Value);
        var currentUserSid = WindowsIdentity.GetCurrent().User?.Value
                             ?? throw new InvalidOperationException("Current user SID is unavailable.");
        using var tempDirectory = new TempDirectory("RunFence_AppxLauncherProgramMain");
        var resultFilePath = Path.Combine(tempDirectory.Path, "result.jsonl");
        var initialObservedProcesses = GetObservedProcessKeys(executablePath);
        var initialCurrentUserProcesses = GetCurrentUserObservedProcessKeys(currentUserSid, executablePath);
        HashSet<ObservedAppxProcess> launchedProcesses = [];

        try
        {
            var exitCode = RunFence.AppxLauncher.Program.LaunchParsed(resultFilePath, executablePath, string.Empty);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode == 0)
            {
                launchedProcesses = WaitForNewCurrentUserProcesses(currentUserSid, executablePath, initialCurrentUserProcesses);
                Assert.NotEmpty(launchedProcesses);
                Assert.False(File.Exists(resultFilePath), "Successful AppX launches should not write an error result file.");
            }
            else
            {
                launchedProcesses = WaitForNewProcesses(executablePath, initialObservedProcesses);
                Assert.NotEmpty(launchedProcesses);
                var result = ReadResult(resultFilePath);
                Assert.False(result.Ok);
                Assert.Equal("VerifyCreatedProcess", result.Stage);
                Assert.Equal(expectedExitCode, result.ExitCode);
                Assert.Contains("Observed owner SIDs:", result.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (launchedProcesses.Count > 0)
                Thread.Sleep(ProcessSurvivalCheckDelay);

            TerminateObservedProcesses(launchedProcesses);
        }
    }

    private static AppxPackageInfo? QueryPackage(string packageName, string expectedPackageFamilyName)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $package = Get-AppxPackage -Name '{{packageName}}' |
                Where-Object { $_.PackageFamilyName -eq '{{expectedPackageFamilyName}}' } |
                Select-Object -First 1
            if ($null -ne $package) {
                $package.InstallLocation
                $package.PackageFamilyName
            }
            """;
        var output = RunPowerShell(script, $"querying package {expectedPackageFamilyName}");
        var lines = output.Split(
            [Environment.NewLine],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            return null;

        return new AppxPackageInfo(lines[^2], lines[^1]);
    }

    private static string ResolvePrimaryExecutablePath(AppxPackageInfo package)
    {
        var manifestPath = Path.Combine(package.InstallLocation, "AppxManifest.xml");
        var manifest = XDocument.Load(manifestPath, LoadOptions.None);
        var application = manifest
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Application", StringComparison.Ordinal))
            .Select(element => new
            {
                Executable = ((string?)element.Attribute("Executable"))?.Trim(),
                EntryPoint = ((string?)element.Attribute("EntryPoint"))?.Trim()
            })
            .FirstOrDefault(application =>
                !string.IsNullOrWhiteSpace(application.Executable) &&
                string.Equals(application.EntryPoint, "Windows.FullTrustApplication", StringComparison.Ordinal));

        application ??= manifest
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Application", StringComparison.Ordinal))
            .Select(element => new
            {
                Executable = ((string?)element.Attribute("Executable"))?.Trim(),
                EntryPoint = ((string?)element.Attribute("EntryPoint"))?.Trim()
            })
            .FirstOrDefault(application => !string.IsNullOrWhiteSpace(application.Executable));

        if (application == null)
            throw new InvalidOperationException($"AppX manifest '{manifestPath}' does not contain an executable application.");

        var executablePath = Path.GetFullPath(Path.Combine(package.InstallLocation, application.Executable!));
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("AppX executable from manifest does not exist.", executablePath);

        return executablePath;
    }

    private static HashSet<ObservedAppxProcess> WaitForNewCurrentUserProcesses(
        string currentUserSid,
        string executablePath,
        IReadOnlySet<ObservedAppxProcessKey> initialProcesses)
    {
        var deadline = DateTime.UtcNow + ProcessAppearanceTimeout;
        do
        {
            var currentProcesses = GetCurrentUserObservedProcesses(currentUserSid, executablePath);
            var newProcesses = currentProcesses
                .Where(process => !initialProcesses.Contains(GetProcessKey(process)))
                .ToHashSet();
            if (newProcesses.Count > 0)
                return newProcesses;

            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);

        return [];
    }

    private static HashSet<ObservedAppxProcess> WaitForNewProcesses(
        string executablePath,
        IReadOnlySet<ObservedAppxProcessKey> initialProcesses)
    {
        var deadline = DateTime.UtcNow + ProcessAppearanceTimeout;
        do
        {
            var currentProcesses = GetObservedProcesses(executablePath);
            var newProcesses = currentProcesses
                .Where(process => !initialProcesses.Contains(GetProcessKey(process)))
                .ToHashSet();
            if (newProcesses.Count > 0)
                return newProcesses;

            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);

        return [];
    }

    private static HashSet<ObservedAppxProcessKey> GetObservedProcessKeys(string executablePath)
        => GetObservedProcesses(executablePath)
            .Select(GetProcessKey)
            .ToHashSet();

    private static HashSet<ObservedAppxProcessKey> GetCurrentUserObservedProcessKeys(string currentUserSid, string executablePath)
        => GetCurrentUserObservedProcesses(currentUserSid, executablePath)
            .Select(GetProcessKey)
            .ToHashSet();

    private static HashSet<ObservedAppxProcess> GetObservedProcesses(string executablePath)
    {
        var scanner = new ProcessSnapshotScanner();
        var imageName = Path.GetFileName(executablePath);
        var normalizedExecutablePath = NormalizeExecutablePath(executablePath);
        var result = new HashSet<ObservedAppxProcess>();
        foreach (var process in scanner.GetProcessesByImageName(imageName))
        {
            if (!TryGetObservedProcess(scanner, process, normalizedExecutablePath, requireExpectedOwnerSid: null, out var observedProcess))
                continue;

            result.Add(observedProcess);
        }

        return result;
    }

    private static HashSet<ObservedAppxProcess> GetCurrentUserObservedProcesses(string currentUserSid, string executablePath)
    {
        var scanner = new ProcessSnapshotScanner();
        var imageName = Path.GetFileName(executablePath);
        var normalizedExecutablePath = NormalizeExecutablePath(executablePath);
        var result = new HashSet<ObservedAppxProcess>();
        foreach (var process in scanner.GetProcessesByImageName(imageName))
        {
            if (!TryGetObservedProcess(scanner, process, normalizedExecutablePath, currentUserSid, out var observedProcess))
                continue;

            result.Add(observedProcess);
        }

        return result;
    }

    private static bool TryGetObservedProcess(
        ProcessSnapshotScanner scanner,
        LightweightProcessInfo process,
        string normalizedExecutablePath,
        string? requireExpectedOwnerSid,
        out ObservedAppxProcess observedProcess)
    {
        observedProcess = default;
        if (!process.CreationTimeUtcTicks.HasValue)
            return false;

        var processPath = scanner.GetExecutablePath(process.ProcessId);
        if (processPath == null)
            return false;

        var normalizedProcessPath = NormalizeExecutablePath(processPath);
        if (!string.Equals(normalizedProcessPath, normalizedExecutablePath, StringComparison.Ordinal))
            return false;

        string? ownerSid = null;
        if (requireExpectedOwnerSid != null)
        {
            var owner = scanner.GetProcessOwner(process.ProcessId, requireExpectedOwnerSid);
            if (owner.Match != ProcessOwnerMatch.ExpectedOwner)
                return false;

            ownerSid = owner.OwnerSid;
        }
        else
        {
            ownerSid = scanner.GetProcessOwner(process.ProcessId, string.Empty).OwnerSid;
        }

        observedProcess = new ObservedAppxProcess(
            process.ProcessId,
            process.CreationTimeUtcTicks.Value,
            normalizedProcessPath,
            ownerSid);
        return true;
    }

    private static void TerminateObservedProcesses(IReadOnlySet<ObservedAppxProcess> launchedProcesses)
    {
        if (launchedProcesses.Count == 0)
            return;

        foreach (var launchedProcessGroup in launchedProcesses.GroupBy(process => process.ExecutablePath, StringComparer.Ordinal))
        {
            var snapshotByKey = GetObservedProcesses(launchedProcessGroup.Key)
                .ToDictionary(GetProcessKey);
            foreach (var launchedProcess in launchedProcessGroup)
            {
                if (!snapshotByKey.TryGetValue(GetProcessKey(launchedProcess), out var currentProcess))
                    continue;
                if (launchedProcess.OwnerSid != null &&
                    !string.Equals(launchedProcess.OwnerSid, currentProcess.OwnerSid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(launchedProcess.ProcessId);
                    if (process.StartTime.ToUniversalTime().Ticks != launchedProcess.CreationTimeUtcTicks)
                        continue;

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static ObservedAppxProcessKey GetProcessKey(ObservedAppxProcess process)
        => new(process.ProcessId, process.CreationTimeUtcTicks, process.ExecutablePath);

    private static string NormalizeExecutablePath(string executablePath)
        => Path.GetFullPath(executablePath).ToUpperInvariant();

    private static AppxLaunchResultPayload ReadResult(string resultFilePath)
    {
        Assert.True(File.Exists(resultFilePath), $"Expected AppX launcher result file '{resultFilePath}' to exist.");
        var payload = JsonSerializer.Deserialize<AppxLaunchResultPayload>(
            File.ReadAllText(resultFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(payload);
        return payload;
    }

    private static string RunPowerShell(string script, string operation)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start powershell.exe.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Timed out while {operation}.");
        }

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"PowerShell failed while {operation}: {standardError.Trim()}");

        return standardOutput;
    }

    private readonly record struct AppxPackageInfo(string InstallLocation, string PackageFamilyName);
    private readonly record struct ObservedAppxProcess(
        int ProcessId,
        long CreationTimeUtcTicks,
        string ExecutablePath,
        string? OwnerSid);
    private readonly record struct ObservedAppxProcessKey(
        int ProcessId,
        long CreationTimeUtcTicks,
        string NormalizedExecutablePath);

}

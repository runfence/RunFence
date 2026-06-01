using System.Text;
using System.Text.Json;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public sealed class PowerShellAppxPackageQueryService(IProcessExecutionService processExecutionService)
    : IAppxPackageQueryService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public IReadOnlyList<RegisteredAppxPackage> QueryPackages()
    {
        var allUsersResult = RunQuery(BuildArguments(allUsers: true));
        if (TryParseSuccessfulResult(allUsersResult, out var packages))
            return packages;

        var currentUserResult = RunQuery(BuildArguments(allUsers: false));
        return TryParseSuccessfulResult(currentUserResult, out packages) ? packages : [];
    }

    private ProcessExecutionResult RunQuery(string arguments)
        => processExecutionService.Run(new ProcessExecutionRequest(
            FileName: "powershell.exe",
            Arguments: arguments,
            Timeout: QueryTimeout,
            KillEntireProcessTreeOnTimeout: true,
            RedirectStandardOutput: true,
            RedirectStandardError: true,
            CancellationToken: CancellationToken.None));

    private static bool TryParseSuccessfulResult(ProcessExecutionResult result, out IReadOnlyList<RegisteredAppxPackage> packages)
    {
        packages = [];
        if (!result.Started
            || result.TimedOut
            || result.ExitCode != 0
            || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return false;
        }

        try
        {
            packages = ParsePackages(result.StandardOutput);
            return packages.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildArguments(bool allUsers)
    {
        var allUsersArgument = allUsers ? " -AllUsers" : string.Empty;
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $ErrorActionPreference = 'Stop'
            Get-AppxPackage__ALL_USERS__ |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_.InstallLocation) } |
                Select-Object PackageFamilyName, PackageFullName, InstallLocation |
                ConvertTo-Json -Compress
            """;
        var resolvedScript = script.Replace("__ALL_USERS__", allUsersArgument, StringComparison.Ordinal);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(resolvedScript));
        return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript;
    }

    private static IReadOnlyList<RegisteredAppxPackage> ParsePackages(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return ParseArray(document.RootElement);

        return document.RootElement.ValueKind == JsonValueKind.Object
            ? ParseSingle(document.RootElement)
            : [];
    }

    private static IReadOnlyList<RegisteredAppxPackage> ParseArray(JsonElement arrayElement)
    {
        var packages = new Dictionary<string, RegisteredAppxPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (!TryParsePackage(element, out var package))
                continue;

            packages.TryAdd(package.InstallLocation, package);
        }

        return packages.Values.ToList();
    }

    private static IReadOnlyList<RegisteredAppxPackage> ParseSingle(JsonElement objectElement)
        => TryParsePackage(objectElement, out var package) ? [package] : [];

    private static bool TryParsePackage(JsonElement element, out RegisteredAppxPackage package)
    {
        package = default!;

        if (!TryGetString(element, "PackageFamilyName", out var familyName)
            || !TryGetString(element, "PackageFullName", out var fullName)
            || !TryGetString(element, "InstallLocation", out var installLocation))
        {
            return false;
        }

        package = new RegisteredAppxPackage(familyName, fullName, installLocation);
        return true;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var propertyElement)
            || propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = propertyElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}

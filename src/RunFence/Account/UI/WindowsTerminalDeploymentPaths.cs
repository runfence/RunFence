using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public sealed class WindowsTerminalDeploymentPaths(IProgramDataKnownPathResolver programDataKnownPathResolver)
{
    public const string DeploymentVersionFileName = ".runfence-deployment-version";
    public const string SharedExecutableFileName = "WindowsTerminal.exe";
    public const string ElevatedExecutableFileName = "WindowsTerminal-Elevated.exe";
    public const string HighIntegrityExecutableFileName = "WindowsTerminal-HighIntegrity.exe";
    public const string IsolatedExecutableFileName = "WindowsTerminal-Isolated.exe";
    public const string LowIntegrityExecutableFileName = "WindowsTerminal-LowIntegrity.exe";
    private const string ExecutableSuffix = ".exe";

    public string SharedRootPath { get; } = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.WindowsTerminalShared);
    public string SharedExecutablePath => Path.Combine(SharedRootPath, SharedExecutableFileName);
    public string SharedDeploymentVersionPath => Path.Combine(SharedRootPath, DeploymentVersionFileName);
    public string SharedHelperPathDirectory => Path.Combine(SharedRootPath, "path");
    public string SharedHelperCommandPath => Path.Combine(SharedHelperPathDirectory, "wt.cmd");
    public string DownloadCacheDirectoryPath { get; } = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.WindowsTerminalCache);
    public string DeploymentWorkRootPath { get; } = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.WindowsTerminalDeploymentWork);

    public string GetCachedZipPath(Version version, string architecture)
        => Path.Combine(DownloadCacheDirectoryPath, $"Microsoft.WindowsTerminal_{version}_{architecture}.zip");

    public string GetOperationWorkRootPath(string operationId)
        => Path.Combine(DeploymentWorkRootPath, NormalizeOperationId(operationId));

    public string GetStagingRootPath(string operationId) => Path.Combine(GetOperationWorkRootPath(operationId), "staging");

    public string GetExtractRootPath(string operationId) => Path.Combine(GetOperationWorkRootPath(operationId), "extract");

    public string GetBackupRootPath(string operationId) => Path.Combine(GetOperationWorkRootPath(operationId), "backup");

    public string GetSharedExecutablePath(PrivilegeLevel privilegeLevel)
        => Path.Combine(SharedRootPath, GetSharedExecutableFileName(privilegeLevel));

    public string GetSharedExecutablePath(PrivilegeLevel? privilegeLevel)
        => privilegeLevel.HasValue
            ? GetSharedExecutablePath(privilegeLevel.Value)
            : SharedExecutablePath;

    public IEnumerable<string> GetSharedExecutablePaths()
    {
        yield return SharedExecutablePath;
        yield return GetSharedExecutablePath(PrivilegeLevel.HighestAllowed);
        yield return GetSharedExecutablePath(PrivilegeLevel.HighIntegrity);
        yield return GetSharedExecutablePath(PrivilegeLevel.Isolated);
        yield return GetSharedExecutablePath(PrivilegeLevel.LowIntegrity);
    }

    public bool IsSharedExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        var fullPath = Path.GetFullPath(executablePath);
        if (IsPrivilegeLaunchExecutablePath(fullPath, PrivilegeLevel.Isolated) ||
            IsPrivilegeLaunchExecutablePath(fullPath, PrivilegeLevel.LowIntegrity))
        {
            return true;
        }

        return GetSharedExecutablePaths().Any(path =>
            string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public string CreatePrivilegeLaunchExecutablePath(PrivilegeLevel privilegeLevel)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(SharedRootPath, $"{GetPrivilegeLaunchExecutablePrefix(privilegeLevel)}{suffix}{ExecutableSuffix}");
    }

    public IEnumerable<string> GetPrivilegeLaunchExecutablePaths(PrivilegeLevel privilegeLevel)
    {
        if (!Directory.Exists(SharedRootPath))
            yield break;

        var prefix = GetPrivilegeLaunchExecutablePrefix(privilegeLevel);
        string[] paths;
        try
        {
            paths = Directory.GetFiles(SharedRootPath, $"{prefix}*{ExecutableSuffix}");
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in paths)
        {
            if (IsPrivilegeLaunchExecutableFileName(Path.GetFileName(path), privilegeLevel))
                yield return path;
        }
    }

    public static bool TryParseCachedZipVersion(string zipPath, string architecture, out Version version)
    {
        var fileName = Path.GetFileName(zipPath);
        var suffix = $"_{architecture}.zip";
        const string prefix = "Microsoft.WindowsTerminal_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            version = new Version();
            return false;
        }

        var versionText = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        if (Version.TryParse(versionText, out var parsedVersion))
        {
            version = parsedVersion;
            return true;
        }

        version = new Version();
        return false;
    }

    private static string NormalizeOperationId(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            operationId is "." or ".." ||
            operationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            operationId.Contains(Path.DirectorySeparatorChar) ||
            operationId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Windows Terminal deployment operation ID is not a valid directory name.");
        }

        return operationId;
    }

    private bool IsPrivilegeLaunchExecutablePath(string executablePath, PrivilegeLevel privilegeLevel)
    {
        var normalizedSharedRootPath = Path.GetFullPath(SharedRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryPath = Path.GetDirectoryName(executablePath);
        if (!string.Equals(
                directoryPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedSharedRootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsPrivilegeLaunchExecutableFileName(Path.GetFileName(executablePath), privilegeLevel);
    }

    private static bool IsPrivilegeLaunchExecutableFileName(string fileName, PrivilegeLevel privilegeLevel)
    {
        var prefix = GetPrivilegeLaunchExecutablePrefix(privilegeLevel);
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(ExecutableSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = fileName.Substring(
            prefix.Length,
            fileName.Length - prefix.Length - ExecutableSuffix.Length);
        if (suffix.Length == 0)
            return false;

        return suffix.All(Uri.IsHexDigit);
    }

    private static string GetPrivilegeLaunchExecutablePrefix(PrivilegeLevel privilegeLevel)
        => privilegeLevel switch
        {
            PrivilegeLevel.Isolated => "WindowsTerminal-Isolated-",
            PrivilegeLevel.LowIntegrity => "WindowsTerminal-LowIntegrity-",
            _ => throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null)
        };

    private static string GetSharedExecutableFileName(PrivilegeLevel privilegeLevel)
        => privilegeLevel switch
        {
            PrivilegeLevel.HighestAllowed => ElevatedExecutableFileName,
            PrivilegeLevel.HighIntegrity => HighIntegrityExecutableFileName,
            PrivilegeLevel.Isolated => IsolatedExecutableFileName,
            PrivilegeLevel.LowIntegrity => LowIntegrityExecutableFileName,
            PrivilegeLevel.Basic => SharedExecutableFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null)
        };
}

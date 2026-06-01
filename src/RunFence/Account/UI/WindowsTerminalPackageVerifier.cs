using System.IO.Compression;
using RunFence.Acl;

namespace RunFence.Account.UI;

public interface IWindowsTerminalPackageVerifier
{
    void VerifyPackage(string zipPath);
}

public sealed class WindowsTerminalPackageVerifier(
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataPathPolicyService programDataPathPolicyService,
    WindowsTerminalDeploymentPaths deploymentPaths,
    IWindowsTerminalExecutableSignatureVerifier executableSignatureVerifier,
    WindowsTerminalPayloadFileInspector payloadFileInspector,
    WindowsTerminalDeploymentDirectoryCleaner deploymentDirectoryCleaner)
    : IWindowsTerminalPackageVerifier
{
    public void VerifyPackage(string zipPath)
    {
        var extractRootPath = Path.Combine(deploymentPaths.DeploymentWorkRootPath, $"verify-{Guid.NewGuid():N}");
        if (!programDataPathPolicyService.IsUnderRoot(extractRootPath))
            throw new InvalidOperationException("Windows Terminal verification path is outside managed ProgramData.");

        programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.WindowsTerminalDeploymentWork);
        Directory.CreateDirectory(extractRootPath);
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var payloadDirectoryName = GetPayloadDirectoryName(archive);
            var executableEntries = GetExecutableEntries(archive, payloadDirectoryName);
            if (!ContainsWindowsTerminalExecutable(executableEntries, payloadDirectoryName))
                throw new InvalidOperationException("WindowsTerminal.exe was not found in the Windows Terminal ZIP.");

            foreach (var executableEntry in executableEntries)
            {
                var executablePath = ExtractEntry(executableEntry, extractRootPath);
                executableSignatureVerifier.VerifyMicrosoftSignedExecutable(executablePath);
            }
        }
        finally
        {
            deploymentDirectoryCleaner.TryDeleteIfExists(extractRootPath);
        }
    }

    private string GetPayloadDirectoryName(ZipArchive archive)
    {
        var topLevelDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var entryParts = GetSafeEntryParts(entry);
            if (entryParts.Length == 0)
                continue;

            topLevelDirectoryNames.Add(entryParts[0]);
        }

        if (topLevelDirectoryNames.Count != 1)
            throw new InvalidOperationException("Windows Terminal ZIP must contain exactly one top-level payload directory.");

        var payloadDirectoryName = topLevelDirectoryNames.Single();
        if (!payloadDirectoryName.StartsWith("terminal-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows Terminal ZIP payload directory must be named terminal-*.");

        return payloadDirectoryName;
    }

    private IReadOnlyList<ZipArchiveEntry> GetExecutableEntries(ZipArchive archive, string payloadDirectoryName)
    {
        var executableEntries = new List<ZipArchiveEntry>();
        foreach (var entry in archive.Entries)
        {
            var normalizedName = GetSafeEntryName(entry);
            if (!IsInsidePayloadDirectory(normalizedName, payloadDirectoryName))
                throw new InvalidOperationException("Windows Terminal ZIP contains entries outside the payload directory.");

            if (string.Equals(normalizedName, payloadDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                !IsDirectoryEntry(entry))
            {
                throw new InvalidOperationException("Windows Terminal ZIP payload root must be a directory.");
            }

            if (IsPortableExecutableEntry(entry))
                executableEntries.Add(entry);
        }

        return executableEntries;
    }

    private bool ContainsWindowsTerminalExecutable(
        IEnumerable<ZipArchiveEntry> executableEntries,
        string payloadDirectoryName)
    {
        var executableEntryPath = payloadDirectoryName + "/WindowsTerminal.exe";
        return executableEntries.Any(entry =>
            string.Equals(GetSafeEntryName(entry), executableEntryPath, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsInsidePayloadDirectory(string entryName, string payloadDirectoryName)
        => string.Equals(entryName, payloadDirectoryName, StringComparison.OrdinalIgnoreCase) ||
           entryName.StartsWith(payloadDirectoryName + "/", StringComparison.OrdinalIgnoreCase);

    private bool IsPortableExecutableEntry(ZipArchiveEntry entry)
    {
        if (IsDirectoryEntry(entry))
            return false;

        var hasExecutableExtension = payloadFileInspector.HasExecutableExtension(entry.FullName);
        if (entry.Length == 0)
            return payloadFileInspector.IsPortableExecutable(Stream.Null, entry.Length, hasExecutableExtension);

        using var entryStream = entry.Open();
        return payloadFileInspector.IsPortableExecutable(entryStream, entry.Length, hasExecutableExtension);
    }

    private bool IsDirectoryEntry(ZipArchiveEntry entry)
        => entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
           entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    private string ExtractEntry(ZipArchiveEntry entry, string extractRootPath)
    {
        var targetPath = ResolveSafeExtractPath(extractRootPath, GetSafeEntryName(entry));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        entry.ExtractToFile(targetPath);
        return targetPath;
    }

    private string ResolveSafeExtractPath(string extractRootPath, string entryName)
    {
        var rootPath = Path.GetFullPath(extractRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(rootPath, entryName.Replace('/', Path.DirectorySeparatorChar)));
        var rootBoundary = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(rootBoundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows Terminal ZIP contains an unsafe entry path.");

        return targetPath;
    }

    private string GetSafeEntryName(ZipArchiveEntry entry)
        => string.Join('/', GetSafeEntryParts(entry));

    private string[] GetSafeEntryParts(ZipArchiveEntry entry)
    {
        var normalizedName = entry.FullName.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedName))
            throw new InvalidOperationException("Windows Terminal ZIP contains an absolute entry path.");

        var parts = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
                throw new InvalidOperationException("Windows Terminal ZIP contains an unsafe entry path.");
        }

        return parts;
    }

}

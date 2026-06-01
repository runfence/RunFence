using System.Text;
using RunFence.Acl;

namespace RunFence.Account.UI;

public interface IWindowsTerminalSharedDeploymentSecurityService
{
    void EnsureSharedDeploymentDirectory();
    void EnsureSharedDeploymentTreeSecurity();
    bool EnsureExecutableCopies();
    bool EnsureHelperFiles();
}

public sealed class WindowsTerminalSharedDeploymentSecurityService(
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataManagedObjectRepairService programDataManagedObjectRepairService,
    WindowsTerminalDeploymentPaths deploymentPaths)
    : IWindowsTerminalSharedDeploymentSecurityService
{
    private const string HelperCommandContent = "@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n";

    public void EnsureSharedDeploymentDirectory()
    {
        programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.WindowsTerminalShared);
    }

    public void EnsureSharedDeploymentTreeSecurity()
    {
        if (!Directory.Exists(deploymentPaths.SharedRootPath))
            throw new InvalidOperationException("Shared Windows Terminal deployment directory is missing.");

        programDataDirectoryProvisioningService.EnsureKnownDirectoryTreeInheritsFromRoot(
            ProgramDataPolicies.WindowsTerminalShared);
    }

    public bool EnsureExecutableCopies()
    {
        if (!File.Exists(deploymentPaths.SharedExecutablePath))
            throw new InvalidOperationException("Shared Windows Terminal deployment is missing WindowsTerminal.exe.");

        var changed = false;
        foreach (var executablePath in deploymentPaths.GetSharedExecutablePaths().Skip(1))
        {
            if (File.Exists(executablePath))
            {
                var ownerChanged = programDataManagedObjectRepairService.EnsureManagedFileOwner(executablePath);
                if (FilesMatch(deploymentPaths.SharedExecutablePath, executablePath))
                {
                    changed |= ownerChanged;
                    continue;
                }
            }

            using var sourceStream = new FileStream(
                deploymentPaths.SharedExecutablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var targetStream = new FileStream(
                executablePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            sourceStream.CopyTo(targetStream);
            changed = true;
        }

        return changed;
    }

    private bool FilesMatch(string sourcePath, string targetPath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var targetInfo = new FileInfo(targetPath);
        if (sourceInfo.Length != targetInfo.Length)
            return false;

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var targetStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sourceBuffer = new byte[81920];
        var targetBuffer = new byte[81920];
        while (true)
        {
            var sourceRead = sourceStream.Read(sourceBuffer, 0, sourceBuffer.Length);
            var targetRead = targetStream.Read(targetBuffer, 0, targetBuffer.Length);
            if (sourceRead != targetRead)
                return false;
            if (sourceRead == 0)
                return true;

            for (var index = 0; index < sourceRead; index++)
            {
                if (sourceBuffer[index] != targetBuffer[index])
                    return false;
            }
        }
    }

    public bool EnsureHelperFiles()
    {
        var helperDirectoryExisted = Directory.Exists(deploymentPaths.SharedHelperPathDirectory);

        if (File.Exists(deploymentPaths.SharedHelperCommandPath))
        {
            var ownerChanged = programDataManagedObjectRepairService.EnsureManagedFileOwner(deploymentPaths.SharedHelperCommandPath);
            var helperContent = File.ReadAllText(deploymentPaths.SharedHelperCommandPath);
            if (string.Equals(helperContent, HelperCommandContent, StringComparison.Ordinal))
            {
                return !helperDirectoryExisted || ownerChanged;
            }
        }

        Directory.CreateDirectory(deploymentPaths.SharedHelperPathDirectory);
        using var stream = new FileStream(
            deploymentPaths.SharedHelperCommandPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(HelperCommandContent);
        return true;
    }
}

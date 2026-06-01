using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;

namespace RunFence.PrefTrans;

public class SettingsTransferStagingService(
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataObjectProvisioner programDataObjectProvisioner,
    IProgramDataKnownPathResolver programDataKnownPathResolver) : ISettingsTransferStagingService
{
    private readonly string sharedTempRoot = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.Temp);

    public string CreateSharedTempFilePath(string extension)
    {
        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? ".tmp" : extension;
        if (!normalizedExtension.StartsWith('.'))
            normalizedExtension = "." + normalizedExtension;

        return Path.Combine(
            CreateSharedTempDirectoryPath(),
            $"settings{normalizedExtension}");
    }

    public string CreateSharedTempDirectoryPath()
        => Path.Combine(sharedTempRoot, $"rfn_transfer_{Guid.NewGuid():N}");

    public string CopyImportFileToRestrictedTemp(string sourcePath, string tempPath, string interactiveSid)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(tempPath))
            throw new ArgumentException("Temporary destination path is required.", nameof(tempPath));
        if (string.IsNullOrWhiteSpace(interactiveSid))
            throw new ArgumentException("Target SID is required.", nameof(interactiveSid));

        var destinationDirectory = Path.GetDirectoryName(tempPath);
        if (string.IsNullOrEmpty(destinationDirectory))
            throw new ArgumentException("Temporary destination path must include a directory.", nameof(tempPath));

        CreateRestrictedExportDirectory(destinationDirectory, interactiveSid);
        File.Copy(sourcePath, tempPath, overwrite: false);

        return tempPath;
    }

    public string CreateRestrictedExportDirectory(string tempDirectoryPath, string interactiveSid)
    {
        if (string.IsNullOrWhiteSpace(tempDirectoryPath))
            throw new ArgumentException("Temporary directory path is required.", nameof(tempDirectoryPath));
        if (string.IsNullOrWhiteSpace(interactiveSid))
            throw new ArgumentException("Interactive SID is required.", nameof(interactiveSid));

        var normalizedPath = Path.GetFullPath(tempDirectoryPath);
        programDataDirectoryProvisioningService.EnsureRoot();
        programDataDirectoryProvisioningService.EnsureKnownDirectory(ProgramDataPolicies.Temp);
        programDataDirectoryProvisioningService.EnsureTraverseOnlyAccess(sharedTempRoot, interactiveSid, ProgramDataDirectoryAclProfile.TrustedOnly);

        programDataObjectProvisioner.CreateOrRepairDirectory(
            new ProgramDataExplicitDirectoryRequest(
                normalizedPath,
                ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl,
                [CreateDirectoryAccess(interactiveSid, FileSystemRights.Modify)],
                ReplaceExistingSecurity: true));

        return normalizedPath;
    }

    public string? TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return path;
            }
        }
        catch
        {
        }

        return null;
    }

    public string? TryDeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return path;
            }
        }
        catch
        {
        }

        return null;
    }

    public void CopyExportFileToDestination(string sourcePath, string destinationPath)
        => File.Copy(sourcePath, destinationPath, overwrite: true);

    private static ProgramDataPrincipalAccess CreateDirectoryAccess(string sid, FileSystemRights rights)
        => new(
            new SecurityIdentifier(sid),
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None);
}

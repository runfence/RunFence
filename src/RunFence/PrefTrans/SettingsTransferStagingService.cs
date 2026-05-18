using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.PrefTrans;

public class SettingsTransferStagingService : ISettingsTransferStagingService
{
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
        => Path.Combine(SharedTempRoot, $"rfn_transfer_{Guid.NewGuid():N}");

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
        var security = BuildRestrictedFileSecurity(interactiveSid);
        using (var destination = FileSystemAclExtensions.Create(
                   new FileInfo(tempPath),
                   FileMode.CreateNew,
                   FileSystemRights.Write,
                   FileShare.None,
                   4096,
                   FileOptions.None,
                   security))
        using (var source = File.OpenRead(sourcePath))
        {
            source.CopyTo(destination);
        }

        return tempPath;
    }

    public string CreateRestrictedExportDirectory(string tempDirectoryPath, string interactiveSid)
    {
        if (string.IsNullOrWhiteSpace(tempDirectoryPath))
            throw new ArgumentException("Temporary directory path is required.", nameof(tempDirectoryPath));
        if (string.IsNullOrWhiteSpace(interactiveSid))
            throw new ArgumentException("Interactive SID is required.", nameof(interactiveSid));

        var normalizedPath = Path.GetFullPath(tempDirectoryPath);
        var parentDirectory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrEmpty(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        var security = BuildRestrictedDirectorySecurity(interactiveSid);
        var directoryInfo = new DirectoryInfo(normalizedPath);
        if (directoryInfo.Exists)
            directoryInfo.SetAccessControl(security);
        else
            directoryInfo.Create(security);

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

    public FileSecurity BuildRestrictedFileSecurity(string interactiveSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(interactiveSid),
            FileSystemRights.Read | FileSystemRights.Synchronize,
            AccessControlType.Allow));

        return security;
    }

    public DirectorySecurity BuildRestrictedDirectorySecurity(string interactiveSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            admins,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(interactiveSid),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        return security;
    }

    public void CopyExportFileToDestination(string sourcePath, string destinationPath)
        => File.Copy(sourcePath, destinationPath, overwrite: true);

    private static string SharedTempRoot
        => Path.Combine(PathConstants.ProgramDataDir, "temp");
}

using System.Security.AccessControl;

namespace RunFence.PrefTrans;

public interface ISettingsTransferStagingService
{
    string CreateSharedTempFilePath(string extension);

    string CreateSharedTempDirectoryPath();

    string CopyImportFileToRestrictedTemp(string sourcePath, string tempPath, string interactiveSid);

    string CreateRestrictedExportDirectory(string tempDirectoryPath, string interactiveSid);

    string? TryDeleteTempFile(string path);

    string? TryDeleteTempDirectory(string path);

    FileSecurity BuildRestrictedFileSecurity(string interactiveSid);

    DirectorySecurity BuildRestrictedDirectorySecurity(string interactiveSid);

    void CopyExportFileToDestination(string sourcePath, string destinationPath);
}

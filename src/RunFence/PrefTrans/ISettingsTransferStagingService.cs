namespace RunFence.PrefTrans;

public interface ISettingsTransferStagingService
{
    string CreateSharedTempFilePath(string extension);

    string CreateSharedTempDirectoryPath();

    string CopyImportFileToRestrictedTemp(string sourcePath, string tempPath, string interactiveSid);

    string CreateRestrictedExportDirectory(string tempDirectoryPath, string interactiveSid);

    string? TryDeleteTempFile(string path);

    string? TryDeleteTempDirectory(string path);

    void CopyExportFileToDestination(string sourcePath, string destinationPath);
}

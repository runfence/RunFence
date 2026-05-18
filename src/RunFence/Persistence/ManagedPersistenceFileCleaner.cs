using RunFence.Core;

namespace RunFence.Persistence;

public class ManagedPersistenceFileCleaner(
    ILoadedGoodBackupStore loadedGoodBackupStore,
    ILoggingService log) : IManagedPersistenceFileCleaner
{
    public void DeletePrimaryAndManagedArtifacts(string primaryFilePath)
    {
        DeleteIfExists(primaryFilePath);

        var backupPath = loadedGoodBackupStore.GetBackupPath(primaryFilePath);
        if (File.Exists(backupPath))
        {
            loadedGoodBackupStore.Delete(primaryFilePath);
            log.Info($"Deleted {backupPath}.");
        }

        var directoryPath = Path.GetDirectoryName(primaryFilePath);
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return;

        var primaryFileName = Path.GetFileName(primaryFilePath);
        var managedArtifactPrefix = primaryFileName + ".";
        foreach (var artifactPath in Directory.EnumerateFiles(directoryPath, managedArtifactPrefix + "*"))
        {
            var artifactFileName = Path.GetFileName(artifactPath);
            if (!artifactFileName.StartsWith(managedArtifactPrefix, StringComparison.Ordinal))
                continue;

            if (!artifactFileName.EndsWith(".rollback", StringComparison.Ordinal)
                && !artifactFileName.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            File.Delete(artifactPath);
            log.Info($"Deleted {artifactPath}.");
        }
    }

    private void DeleteIfExists(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        File.Delete(filePath);
        log.Info($"Deleted {filePath}.");
    }
}

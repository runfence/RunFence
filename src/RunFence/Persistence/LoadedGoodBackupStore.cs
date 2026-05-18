using System.Security;

namespace RunFence.Persistence;

public class LoadedGoodBackupStore(
    IPersistenceAtomicFileWriter atomicFileWriter,
    IPersistenceFileSecurityMirror persistenceFileSecurityMirror) : ILoadedGoodBackupStore
{
    public bool TryPreserveCurrentFile(string targetPath, out string? warning)
    {
        try
        {
            var sourceSecurity = persistenceFileSecurityMirror.CaptureFileSecurity(targetPath);
            atomicFileWriter.AtomicCopy(targetPath, GetBackupPath(targetPath), sourceSecurity);
            warning = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            warning = $"Could not preserve loaded-good backup for '{targetPath}': {ex.Message}";
            return false;
        }
    }

    public bool Exists(string targetPath)
        => File.Exists(GetBackupPath(targetPath));

    public void Restore(string targetPath)
        => atomicFileWriter.AtomicWrite(targetPath, File.ReadAllBytes(GetBackupPath(targetPath)));

    public void Delete(string targetPath)
    {
        var backupPath = GetBackupPath(targetPath);
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    public string GetBackupPath(string targetPath) => targetPath + ".lastgood";
}

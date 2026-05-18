using System.Security.AccessControl;

namespace RunFence.Persistence;

public interface IPersistenceAtomicFileWriter
{
    void AtomicWrite(string targetPath, byte[] data);
    void AtomicWrite(string targetPath, byte[] data, FileSecurity? finalSecurity);
    void AtomicCopy(string sourcePath, string targetPath, FileSecurity? finalSecurity);
    void AtomicWriteBatch(IReadOnlyList<(string path, byte[] data)> files);
}

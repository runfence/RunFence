namespace RunFence.Persistence;

public interface ILoadedGoodBackupStore
{
    bool TryPreserveCurrentFile(string targetPath, out string? warning);
    bool Exists(string targetPath);
    void Restore(string targetPath);
    void Delete(string targetPath);
    string GetBackupPath(string targetPath);
}

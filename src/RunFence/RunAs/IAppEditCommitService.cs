using RunFence.Core.Models;

namespace RunFence.RunAs;

public interface IAppEditCommitService
{
    bool Commit(AppEntry newApp, AppEntry? previousApp, string? configPath);
    void Rollback(AppEntry originalApp, string? configPath);
    void SaveAllConfigs();
}

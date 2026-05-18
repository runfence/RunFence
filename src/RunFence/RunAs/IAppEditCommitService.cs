using RunFence.Core.Models;

namespace RunFence.RunAs;

public interface IAppEditCommitService
{
    RunAsAppEntryPersistenceResult Commit(AppEntry newApp, AppEntry? previousApp, string? configPath);
}

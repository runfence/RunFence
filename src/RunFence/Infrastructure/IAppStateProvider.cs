using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IAppStateProvider
{
    bool IsOperationInProgress { get; }
    bool IsModalOpen { get; }
    bool IsShuttingDown { get; }
    AppDatabase Database { get; }
}
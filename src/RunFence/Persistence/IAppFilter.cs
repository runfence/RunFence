using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IAppFilter
{
    AppDatabase FilterForMainConfig(AppDatabase database);
}
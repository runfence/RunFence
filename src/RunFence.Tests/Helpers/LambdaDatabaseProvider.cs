using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed class LambdaDatabaseProvider(Func<AppDatabase> getDatabase) : IDatabaseProvider
{
    public AppDatabase GetDatabase() => getDatabase();
}

using RunFence.Core.Models;

namespace RunFence.Persistence;

/// <summary>
/// Provides the current <see cref="AppDatabase"/> instance.
/// Allows background services to access the live database without capturing a closure.
/// </summary>
public interface IDatabaseProvider
{
    AppDatabase GetDatabase();
}

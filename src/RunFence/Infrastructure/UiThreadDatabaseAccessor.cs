using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Infrastructure;

/// <summary>
/// Marshals database reads and writes to the UI thread via <see cref="IUiThreadInvoker"/>.
/// Consumers inject this single dependency instead of the separate
/// <see cref="IDatabaseProvider"/> + <see cref="IUiThreadInvoker"/> pair.
/// </summary>
public class UiThreadDatabaseAccessor(IDatabaseProvider databaseProvider, Func<IUiThreadInvoker> uiThreadInvokerFactory)
{
    public T Read<T>(Func<AppDatabase, T> reader)
        => uiThreadInvokerFactory().Invoke(() => reader(databaseProvider.GetDatabase()));

    public void Read(Action<AppDatabase> reader)
        => uiThreadInvokerFactory().Invoke(() => reader(databaseProvider.GetDatabase()));

    public T Write<T>(Func<AppDatabase, T> writer)
        => uiThreadInvokerFactory().Invoke(() => writer(databaseProvider.GetDatabase()));

    public void Write(Action<AppDatabase> writer)
        => uiThreadInvokerFactory().Invoke(() => writer(databaseProvider.GetDatabase()));

    public AppDatabase CreateSnapshot()
        => Read(db => db.CreateSnapshot());
}

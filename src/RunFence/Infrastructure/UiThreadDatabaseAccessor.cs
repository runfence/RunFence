using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Infrastructure;

/// <summary>
/// Marshals database reads and writes to the UI thread via <see cref="IUiThreadInvoker"/>.
/// Consumers inject this single dependency instead of the separate
/// <see cref="IDatabaseProvider"/> + <see cref="IUiThreadInvoker"/> pair.
/// </summary>
public class UiThreadDatabaseAccessor(IDatabaseProvider databaseProvider, IUiThreadInvoker uiThreadInvoker)
{
    public T Read<T>(Func<AppDatabase, T> reader)
        => uiThreadInvoker.Invoke(() => reader(databaseProvider.GetDatabase()));

    public T Write<T>(Func<AppDatabase, T> writer)
        => uiThreadInvoker.Invoke(() => writer(databaseProvider.GetDatabase()));

    public void Write(Action<AppDatabase> writer)
        => uiThreadInvoker.Invoke(() => writer(databaseProvider.GetDatabase()));

    public AppDatabase CreateSnapshot()
        => Read(db => db.CreateSnapshot());
}

using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Tests.Helpers;

public sealed class FakeHandlerMappingDialogPersistence : IHandlerMappingDialogPersistence
{
    private readonly AppDatabase _database;
    private readonly Action _saveDatabase;

    public FakeHandlerMappingDialogPersistence(AppDatabase database, Action? saveDatabase = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _saveDatabase = saveDatabase ?? (() => { });
    }

    public int GetDatabaseCalls { get; private set; }
    public int SaveDatabaseCalls { get; private set; }

    public AppDatabase GetDatabase()
    {
        GetDatabaseCalls++;
        return _database;
    }

    public void SaveDatabase()
    {
        SaveDatabaseCalls++;
        _saveDatabase();
    }
}

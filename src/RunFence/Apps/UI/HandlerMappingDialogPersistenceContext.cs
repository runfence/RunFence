using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

public sealed class HandlerMappingDialogPersistenceContext : IHandlerMappingDialogPersistence
{
    private readonly AppDatabase _database;
    private readonly Action _saveDatabase;

    public HandlerMappingDialogPersistenceContext(AppDatabase database, Action saveDatabase)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _saveDatabase = saveDatabase ?? throw new ArgumentNullException(nameof(saveDatabase));
    }

    public AppDatabase GetDatabase() => _database;

    public void SaveDatabase() => _saveDatabase();
}

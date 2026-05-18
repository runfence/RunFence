using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

public interface IHandlerMappingDialogPersistence
{
    AppDatabase GetDatabase();
    void SaveDatabase();
}

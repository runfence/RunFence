using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IGrantIntentStoreProvider
{
    IGrantIntentStore MainStore { get; }

    IReadOnlyList<IGrantIntentStore> GetLoadedStores();

    IGrantIntentStore ResolveStore(string? configPath);

    IGrantIntentStore RegisterAdditionalStore(
        string configPath,
        List<AppConfigAccountEntry> accounts);

    void UnregisterAdditionalStore(string configPath);
}

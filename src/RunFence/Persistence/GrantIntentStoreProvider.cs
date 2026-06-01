using RunFence.Core.Models;
using RunFence.Persistence.UI;

namespace RunFence.Persistence;

public class GrantIntentStoreProvider(
    MainGrantIntentStore mainStore,
    ConfigSaveOrchestrator configSaveOrchestrator,
    GrantIntentOwnershipProjectionService ownershipProjection)
    : IGrantIntentStoreProvider
{
    private readonly Dictionary<string, AdditionalGrantIntentStore> _additionalStores =
        new(StringComparer.OrdinalIgnoreCase);

    public IGrantIntentStore MainStore => mainStore;

    public IReadOnlyList<IGrantIntentStore> GetLoadedStores()
        => [MainStore, .. GetAdditionalStoresOrdered()];

    public IGrantIntentStore ResolveStore(string? configPath)
    {
        if (configPath == null)
            return MainStore;

        var normalizedPath = AppConfigPathHelper.NormalizePath(configPath);
        if (_additionalStores.TryGetValue(normalizedPath, out var store))
            return store;

        throw new InvalidOperationException($"Additional config store is not loaded: {normalizedPath}");
    }

    public IGrantIntentStore RegisterAdditionalStore(
        string configPath,
        List<AppConfigAccountEntry> accounts)
    {
        var store = new AdditionalGrantIntentStore(
            configPath,
            accounts,
            configSaveOrchestrator,
            ownershipProjection);
        _additionalStores[store.ConfigPath] = store;
        ownershipProjection.RegisterAdditionalConfig(
            store.ConfigPath,
            accounts);
        return store;
    }

    public void UnregisterAdditionalStore(string configPath)
    {
        var normalizedPath = AppConfigPathHelper.NormalizePath(configPath);
        _additionalStores.Remove(normalizedPath);
        ownershipProjection.UnregisterAdditionalConfig(normalizedPath);
    }

    private IReadOnlyList<IGrantIntentStore> GetAdditionalStoresOrdered()
        => _additionalStores.Values
            .OrderBy(store => store.ConfigPath, StringComparer.OrdinalIgnoreCase)
            .Cast<IGrantIntentStore>()
            .ToList();
}

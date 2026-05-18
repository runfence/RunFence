using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed class AppConfigRuntimeStateSnapshot
{
    public AppConfigRuntimeStateSnapshot(
        IReadOnlyDictionary<string, string> appConfigMap,
        IReadOnlyList<string> loadedPaths,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>> handlerMappingsByConfigPath)
        : this(
            appConfigMap,
            loadedPaths,
            handlerMappingsByConfigPath,
            GrantIntentOwnershipProjectionSnapshot.Empty)
    {
    }

    internal AppConfigRuntimeStateSnapshot(
        IReadOnlyDictionary<string, string> appConfigMap,
        IReadOnlyList<string> loadedPaths,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>> handlerMappingsByConfigPath,
        GrantIntentOwnershipProjectionSnapshot ownershipProjectionSnapshot)
    {
        AppConfigMap = appConfigMap;
        LoadedPaths = loadedPaths;
        HandlerMappingsByConfigPath = handlerMappingsByConfigPath;
        OwnershipProjectionSnapshot = ownershipProjectionSnapshot;
    }

    internal IReadOnlyDictionary<string, string> AppConfigMap { get; }
    internal IReadOnlyList<string> LoadedPaths { get; }
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>> HandlerMappingsByConfigPath { get; }
    internal GrantIntentOwnershipProjectionSnapshot OwnershipProjectionSnapshot { get; }
}

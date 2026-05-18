namespace RunFence.Persistence;

internal sealed record AppConfigIndexStateSnapshot(
    IReadOnlyDictionary<string, string> AppConfigMap,
    IReadOnlyList<string> LoadedPaths);

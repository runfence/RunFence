using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed record AdditionalConfigLoadData(
    string NormalizedPath,
    List<AppEntry> Apps,
    List<AppConfigAccountEntry> Accounts,
    Dictionary<string, HandlerMappingEntry> HandlerMappings,
    bool SkipCommit = false);

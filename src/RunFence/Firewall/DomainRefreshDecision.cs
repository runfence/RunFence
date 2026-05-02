namespace RunFence.Firewall;

public sealed record DomainRefreshDecision(
    bool ShouldRefreshRules,
    bool WasDirty,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResolvedDomains,
    IReadOnlyCollection<string> DomainsToClearOnSuccess);

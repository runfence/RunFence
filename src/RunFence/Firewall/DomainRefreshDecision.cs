namespace RunFence.Firewall;

public sealed record DomainRefreshDecision(
    bool ShouldRefreshRules,
    bool DnsChanged,
    bool WasDirty,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResolvedDomains,
    IReadOnlyCollection<string> DomainsToClearOnSuccess);

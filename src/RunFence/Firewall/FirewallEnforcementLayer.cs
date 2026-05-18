namespace RunFence.Firewall;

public enum FirewallEnforcementLayer
{
    AccountRules,
    WfpFilters,
    AuditPolicy,
    DnsRefresh,
    EndpointScan,
    GlobalIcmp
}

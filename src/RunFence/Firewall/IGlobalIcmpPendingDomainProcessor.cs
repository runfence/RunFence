namespace RunFence.Firewall;

/// <summary>
/// Processes pending domain resolutions for the global ICMP block,
/// marking dirty state and requesting a DNS refresh if needed.
/// </summary>
public interface IGlobalIcmpPendingDomainProcessor
{
    void ProcessPendingDomains(IReadOnlyList<FirewallPendingDomainResolution> pendingDomains);
}

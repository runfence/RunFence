namespace RunFence.Firewall.Wfp;

/// <summary>
/// Manages a pair of global (protocol-only, no user identity) WFP BLOCK filters at
/// ALE_AUTH_CONNECT V4/V6 that block ICMP to specific internet address ranges.
///
/// This is necessary because IcmpSendEcho2 (the only ICMP path available to non-admin
/// accounts) carries SYSTEM/Everyone identity at ALE, making per-user blocking via
/// FWPM_CONDITION_ALE_USER_ID impossible. The filter blocks outbound ICMP to internet
/// addresses for all users while any account has internet blocked, preventing ICMP
/// tunneling (IcmpSendEcho2 with custom payloads to a cooperating server).
///
/// Raw-socket ICMP from elevated processes is blocked per-user by <see cref="IWfpIcmpBlocker"/>.
/// </summary>
public interface IWfpGlobalIcmpBlocker
{
    /// <summary>
    /// Installs global ICMP BLOCK filters at ALE_AUTH_CONNECT for the specified address ranges,
    /// replacing any previously installed global ICMP filters. Empty lists remove the filters.
    /// Each CIDR string is in "a.b.c.d/prefix" or "::addr/prefix" format.
    /// </summary>
    void Apply(IReadOnlyList<string> ipv4CidrRanges, IReadOnlyList<string> ipv6CidrRanges);
}

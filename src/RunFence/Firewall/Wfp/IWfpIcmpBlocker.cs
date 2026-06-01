namespace RunFence.Firewall.Wfp;

public interface IWfpIcmpBlocker
{
    /// <summary>
    /// Registers or unregisters the account SID for the per-user raw-socket ICMP blocker.
    /// A shared protocol-only WFP BLOCK filter at ALE_AUTH_CONNECT is installed when at least
    /// one account needs this per-user ICMP block, and removed when none do. This filter is
    /// separate from <see cref="IWfpGlobalIcmpBlocker"/>, which enforces the global CIDR-based
    /// ICMP policy for internet-blocked accounts.
    ///
    /// Per-user filtering is impossible because IcmpSendEcho2 (kernel IOCTL path used by
    /// non-admin ping) carries SYSTEM/Everyone identity at ALE, not the calling user's SID.
    /// FWPM_CONDITION_ALE_USER_ID cannot distinguish users for ICMP traffic.
    /// </summary>
    void Apply(string sid, bool block);
}

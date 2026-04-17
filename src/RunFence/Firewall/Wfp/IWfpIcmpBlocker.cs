namespace RunFence.Firewall.Wfp;

public interface IWfpIcmpBlocker
{
    /// <summary>
    /// Registers or unregisters the account SID for ICMP blocking. A shared protocol-only
    /// WFP BLOCK filter at ALE_AUTH_CONNECT is installed when ≥1 account needs ICMP blocked,
    /// and removed when none do. The filter blocks all outbound ICMP on the machine.
    ///
    /// Per-user filtering is impossible because IcmpSendEcho2 (kernel IOCTL path used by
    /// non-admin ping) carries SYSTEM/Everyone identity at ALE, not the calling user's SID.
    /// FWPM_CONDITION_ALE_USER_ID cannot distinguish users for ICMP traffic.
    /// </summary>
    void Apply(string sid, bool block);
}

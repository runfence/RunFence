namespace RunFence.Firewall.Wfp;

public interface IWfpLocalhostBlocker
{
    /// <summary>
    /// Adds or removes WFP filters that block outbound loopback connections (IPv4 127.0.0.0/8
    /// and IPv6 ::1) for the specified account SID. Filters are persistent (survive restarts).
    /// Uses deterministic filter keys derived from the SID, so calling Apply again updates
    /// existing filters in place (delete + re-add).
    /// <para>
    /// <paramref name="allowedPorts"/> entries — single ports (e.g. "8080") or ranges (e.g. "3000-3010")
    /// — are exempted from the loopback block. The static complement covers 1–65535 minus the
    /// exemption list. Ignored when <paramref name="block"/> is false.
    /// </para>
    /// </summary>
    void Apply(string sid, bool block, IReadOnlyList<string> allowedPorts);

    /// <summary>
    /// Updates the dynamically-discovered cross-user ephemeral port ranges to block for the
    /// specified SID. No-op if the SID has no active block or ranges are unchanged.
    /// Rebuilds WFP filters atomically within a transaction when changed.
    /// </summary>
    void UpdateEphemeralPorts(string sid, IReadOnlyList<PortRange> ephemeralBlockedRanges);
}

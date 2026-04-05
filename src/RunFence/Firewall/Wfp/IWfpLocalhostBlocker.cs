namespace RunFence.Firewall.Wfp;

public interface IWfpLocalhostBlocker
{
    /// <summary>
    /// Adds or removes WFP filters that block outbound loopback connections (IPv4 127.0.0.0/8
    /// and IPv6 ::1) for the specified account SID. Filters are persistent (survive restarts).
    /// Uses deterministic filter keys derived from the SID, so calling Apply again updates
    /// existing filters in place (delete + re-add).
    /// </summary>
    void Apply(string sid, bool block);
}
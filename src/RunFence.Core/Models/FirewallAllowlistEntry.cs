namespace RunFence.Core.Models;

public class FirewallAllowlistEntry
{
    public string Value { get; set; } = ""; // IP, CIDR, or domain
    public bool IsDomain { get; set; } // true = needs DNS resolution
}
namespace RunFence.Firewall;

public sealed record FirewallPendingDomainResolution(string Sid, string Domain)
{
    public string DeduplicationKey => $"{Sid}\0{Domain}";
}

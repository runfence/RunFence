namespace RunFence.Firewall;

public sealed record EnforceAllResult(
    IReadOnlyList<FirewallEnforcementFailure> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}

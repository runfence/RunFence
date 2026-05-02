namespace RunFence.Firewall;

public class FirewallApplyException(FirewallApplyPhase phase, string sid, Exception innerException)
    : Exception($"Firewall apply failed during {phase} for SID '{sid}'.", innerException)
{
    public FirewallApplyPhase Phase { get; } = phase;
    public string CauseMessage => InnerException?.Message ?? Message;
}

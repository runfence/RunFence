namespace RunFence.Firewall;

public class FirewallApplyException : Exception
{
    public FirewallApplyException(FirewallApplyPhase phase, string sid, Exception innerException)
        : base($"Firewall apply failed during {phase} for SID '{sid}'.", innerException)
    {
        Phase = phase;
        Sid = sid;
    }

    public FirewallApplyPhase Phase { get; }
    public string Sid { get; }
    public string CauseMessage => InnerException?.Message ?? Message;
}

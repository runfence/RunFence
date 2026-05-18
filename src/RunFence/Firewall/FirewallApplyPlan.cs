namespace RunFence.Firewall;

public sealed class FirewallApplyPlan(
    IReadOnlyList<FirewallApplyPlanPhase> phases,
    bool requiresNoOpSuccessEntries,
    bool updatesAccountRetryState,
    bool updatesGlobalIcmpRetryState)
{
    public IReadOnlyList<FirewallApplyPlanPhase> Phases { get; } = phases.ToList();
    public bool RequiresNoOpSuccessEntries { get; } = requiresNoOpSuccessEntries;
    public bool UpdatesAccountRetryState { get; } = updatesAccountRetryState;
    public bool UpdatesGlobalIcmpRetryState { get; } = updatesGlobalIcmpRetryState;
    public bool PersistenceAlreadyHappened { get; private set; }

    internal void MarkPersistenceCompleted()
    {
        PersistenceAlreadyHappened = true;
    }
}

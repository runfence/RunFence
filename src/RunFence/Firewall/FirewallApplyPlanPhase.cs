using RunFence.Core.Models;

namespace RunFence.Firewall;

public sealed class FirewallApplyPlanPhase(
    FirewallApplyPlanPhaseKind kind,
    bool persistConfigBeforeExecution,
    bool shouldPersist,
    FirewallAccountSettings targetSettings,
    FirewallOperation? accountOperation,
    FirewallOperation? globalIcmpOperation)
{
    public FirewallApplyPlanPhaseKind Kind { get; } = kind;
    public bool PersistConfigBeforeExecution { get; } = persistConfigBeforeExecution;
    public bool ShouldPersist { get; } = shouldPersist;
    public FirewallAccountSettings TargetSettings { get; } = targetSettings.Clone();
    public FirewallOperation? AccountOperation { get; } = CloneOperation(accountOperation);
    public FirewallOperation? GlobalIcmpOperation { get; } = CloneOperation(globalIcmpOperation);

    private static FirewallOperation? CloneOperation(FirewallOperation? operation)
        => operation is null
            ? null
            : new FirewallOperation(
                operation.Layer,
                operation.AccountSid,
                operation.PreviousSettings.Clone(),
                operation.RequestedSettings.Clone());
}

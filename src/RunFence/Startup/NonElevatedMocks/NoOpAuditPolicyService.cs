#pragma warning disable CS9113
using RunFence.Firewall;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpAuditPolicyService(IAuditPolicyService real) : IAuditPolicyService
{
    public AuditPolicyResult EnableBlockedConnectionAuditing() =>
        new(AuditPolicyStatus.Succeeded, true, true, null, IsRetryable: false);

    public AuditPolicyResult DisableBlockedConnectionAuditing() =>
        new(AuditPolicyStatus.Succeeded, false, false, null, IsRetryable: false);

    public AuditPolicyResult ReadBlockedConnectionAuditingState() =>
        new(AuditPolicyStatus.Succeeded, false, false, null, IsRetryable: false);
}

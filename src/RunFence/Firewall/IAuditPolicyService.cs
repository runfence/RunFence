namespace RunFence.Firewall;

public interface IAuditPolicyService
{
    AuditPolicyResult EnableBlockedConnectionAuditing();
    AuditPolicyResult DisableBlockedConnectionAuditing();
    AuditPolicyResult ReadBlockedConnectionAuditingState();
}

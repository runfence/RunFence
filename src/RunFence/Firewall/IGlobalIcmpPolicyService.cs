using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IGlobalIcmpPolicyService
{
    GlobalIcmpEnforcementPlan CreateGlobalIcmpPlan(
        AppDatabase database,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    GlobalIcmpEnforcementPlan CreateGlobalIcmpPlan(
        GlobalIcmpPolicyInput input,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    void EnforceGlobalIcmpBlock(GlobalIcmpEnforcementPlan plan);

    void EnforceGlobalIcmpBlock(
        AppDatabase database,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache);

    void RemoveGlobalIcmpBlock();
}

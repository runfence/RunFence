namespace RunFence.Firewall;

public class FirewallApplyRetryCoordinator(
    FirewallEnforcementRetryState retryState,
    IFirewallDomainRefreshRequester refreshRequester)
{
    private const string GlobalScopeKey = "__global__";

    public void MarkAccountEnforcementRetryPending(string sid, string errorMessage)
    {
        retryState.MarkRetryPending(FirewallEnforcementLayer.AccountRules, sid, errorMessage, "Retry account firewall enforcement.");
        retryState.MarkRetryPending(FirewallEnforcementLayer.WfpFilters, sid, errorMessage, "Retry WFP firewall enforcement.");
        refreshRequester.RequestRefresh();
    }

    public void MarkGlobalIcmpRetryPending(string errorMessage)
    {
        retryState.MarkRetryPending(
            FirewallEnforcementLayer.GlobalIcmp,
            GlobalScopeKey,
            errorMessage,
            "Retry global ICMP enforcement.");
    }
}

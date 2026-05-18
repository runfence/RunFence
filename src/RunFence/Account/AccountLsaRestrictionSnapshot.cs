namespace RunFence.Account;

public sealed record AccountLsaRestrictionSnapshot(
    bool DenyNetworkLogon,
    bool DenyRemoteInteractiveLogon,
    bool DenyBatchLogon,
    bool DenyServiceLogon)
{
    public bool HasAnyNetworkLoginRestriction => DenyNetworkLogon || DenyRemoteInteractiveLogon;
    public bool HasFullNetworkLoginRestriction => DenyNetworkLogon && DenyRemoteInteractiveLogon;
    public bool HasAnyBackgroundAutorunRestriction => DenyBatchLogon || DenyServiceLogon;
    public bool HasFullBackgroundAutorunRestriction => DenyBatchLogon && DenyServiceLogon;

    public bool? NetworkLoginState => HasFullNetworkLoginRestriction ? true : HasAnyNetworkLoginRestriction ? null : false;
    public bool? BackgroundAutorunState => HasFullBackgroundAutorunRestriction ? true : HasAnyBackgroundAutorunRestriction ? null : false;
}

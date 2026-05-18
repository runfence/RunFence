namespace RunFence.Account;

public sealed record AccountRestrictionSnapshot(
    bool GroupPolicyLogonBlocked,
    bool HiddenAccountRegistrySet,
    AccountLsaRestrictionSnapshot LsaRestrictions)
{
    public bool NoLogonBlockedFailClosed => GroupPolicyLogonBlocked || HiddenAccountRegistrySet;
    public bool NetworkLoginBlockedFailClosed => LsaRestrictions.HasAnyNetworkLoginRestriction;
    public bool BackgroundAutorunBlockedFailClosed => LsaRestrictions.HasAnyBackgroundAutorunRestriction;
}

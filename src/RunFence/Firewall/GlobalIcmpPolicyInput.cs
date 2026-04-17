using RunFence.Core.Models;

namespace RunFence.Firewall;

public sealed record GlobalIcmpPolicyInput(
    bool BlockIcmpWhenInternetBlocked,
    IReadOnlyList<AccountEntry> BlockedAccounts);

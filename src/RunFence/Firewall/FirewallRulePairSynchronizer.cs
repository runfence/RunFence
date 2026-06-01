using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public sealed class FirewallRulePairSynchronizer(
    IFirewallRuleManager ruleManager,
    FirewallAddressExclusionBuilder addressBuilder)
{
    private const int DirectionOutbound = 2;
    private const int ActionBlock = 0;
    private const int ProtocolAny = 256;

    public IReadOnlyList<FirewallPendingDomainResolution> ApplyInternetRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => ApplyOrRemoveRulePair(
            sid,
            FirewallRuleNames.InternetIPv4RuleName(username),
            FirewallRuleNames.InternetIPv6RuleName(username),
            !settings.AllowInternet,
            () => addressBuilder.ComputeInternetAddresses(sid, settings, resolvedDomainsCache),
            existing);

    public IReadOnlyList<FirewallPendingDomainResolution> ApplyLanRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => ApplyOrRemoveRulePair(
            sid,
            FirewallRuleNames.LanIPv4RuleName(username),
            FirewallRuleNames.LanIPv6RuleName(username),
            !settings.AllowLan,
            () => addressBuilder.ComputeLanAddresses(sid, settings, resolvedDomainsCache),
            existing);

    public void RemoveLocalhostLegacyRules(string username, IReadOnlyList<FirewallRuleInfo> existing)
    {
        RemoveRuleIfExists(FirewallRuleNames.LocalhostIPv4RuleName(username), existing);
        RemoveRuleIfExists(FirewallRuleNames.LocalhostIPv6RuleName(username), existing);
    }

    public IReadOnlyList<FirewallPendingDomainResolution> ApplyLocalAddressRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing)
        => ApplyOrRemoveRulePair(
            sid,
            FirewallRuleNames.LocalAddressIPv4RuleName(username),
            FirewallRuleNames.LocalAddressIPv6RuleName(username),
            !settings.AllowLocalhost,
            () =>
            {
                var addresses = addressBuilder.ComputeLocalAddressRanges();
                return new FirewallAddressComputationResult(addresses.IPv4Address, addresses.IPv6Address, []);
            },
            existing);

    public bool RefreshAllowlistRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        if (settings is { AllowInternet: true, AllowLan: true })
            return false;

        bool changed = false;

        if (!settings.AllowInternet)
        {
            changed |= RefreshRulePair(
                sid,
                FirewallRuleNames.InternetIPv4RuleName(username),
                FirewallRuleNames.InternetIPv6RuleName(username),
                addressBuilder.ComputeInternetAddresses(sid, settings, resolvedDomainsCache),
                existing);
        }

        if (!settings.AllowLan)
        {
            changed |= RefreshRulePair(
                sid,
                FirewallRuleNames.LanIPv4RuleName(username),
                FirewallRuleNames.LanIPv6RuleName(username),
                addressBuilder.ComputeLanAddresses(sid, settings, resolvedDomainsCache),
                existing);
        }

        return changed;
    }

    public bool RefreshLocalAddressRules(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        if (settings.AllowLocalhost)
            return false;

        var addresses = addressBuilder.ComputeLocalAddressRanges();
        var computation = new FirewallAddressComputationResult(
            addresses.IPv4Address,
            addresses.IPv6Address,
            []);
        return RefreshRulePair(
            sid,
            FirewallRuleNames.LocalAddressIPv4RuleName(username),
            FirewallRuleNames.LocalAddressIPv6RuleName(username),
            computation,
            existing);
    }

    private IReadOnlyList<FirewallPendingDomainResolution> ApplyOrRemoveRulePair(
        string sid,
        string ipv4Name,
        string ipv6Name,
        bool shouldBlock,
        Func<FirewallAddressComputationResult> computeAddresses,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        if (!shouldBlock)
        {
            RemoveRuleIfExists(ipv4Name, existing);
            RemoveRuleIfExists(ipv6Name, existing);
            return [];
        }

        var addresses = computeAddresses();
        EnsureRule(ipv4Name, sid, addresses.IPv4Address, existing);
        EnsureRule(ipv6Name, sid, addresses.IPv6Address, existing);
        return addresses.PendingDomains;
    }

    private bool RefreshRulePair(
        string sid,
        string ipv4Name,
        string ipv6Name,
        FirewallAddressComputationResult addresses,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        var ipv4Rule = existing.FirstOrDefault(r => string.Equals(r.Name, ipv4Name, StringComparison.OrdinalIgnoreCase));
        var ipv6Rule = existing.FirstOrDefault(r => string.Equals(r.Name, ipv6Name, StringComparison.OrdinalIgnoreCase));
        var staleRulesRemoved = RemoveStaleRulePairVariants(existing, ipv4Name, ipv6Name);

        bool changed = staleRulesRemoved;
        changed |= RefreshSingleRule(sid, ipv4Name, addresses.IPv4Address, ipv4Rule);
        changed |= RefreshSingleRule(sid, ipv6Name, addresses.IPv6Address, ipv6Rule);
        return changed;
    }

    private bool RemoveStaleRulePairVariants(
        IReadOnlyList<FirewallRuleInfo> existing,
        string ipv4Name,
        string ipv6Name)
    {
        var ipv4Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv4Name);
        var ipv6Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv6Name);
        var staleRules = existing
            .Where(r =>
                (r.Name.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase) ||
                 r.Name.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(r.Name, ipv4Name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.Name, ipv6Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var staleRule in staleRules)
            ruleManager.RemoveRule(staleRule.Name);

        return staleRules.Count > 0;
    }

    private bool RefreshSingleRule(string sid, string targetName, string newAddress, FirewallRuleInfo? existingRule)
    {
        if (existingRule == null)
        {
            if (string.IsNullOrEmpty(newAddress))
                return false;

            ruleManager.AddRule(BuildRuleInfo(targetName, sid, newAddress));
            return true;
        }

        if (string.IsNullOrEmpty(newAddress))
        {
            ruleManager.RemoveRule(existingRule.Name);
        }
        else
        {
            var desiredRule = BuildRuleInfo(targetName, sid, newAddress);
            if (existingRule == desiredRule)
                return false;

            ruleManager.UpdateRule(existingRule.Name, desiredRule);
        }

        return true;
    }

    private void EnsureRule(
        string name,
        string sid,
        string remoteAddress,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        var namePrefix = FirewallRuleNames.GetRuleNamePrefix(name);
        var existingRule = existing.FirstOrDefault(r =>
            r.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(remoteAddress))
        {
            if (existingRule != null)
                RemoveRuleIfExists(name, existing);
            return;
        }

        var info = BuildRuleInfo(name, sid, remoteAddress);

        if (existingRule == null)
            ruleManager.AddRule(info);
        else if (existingRule != info)
            ruleManager.UpdateRule(existingRule.Name, info);
    }

    private void RemoveRuleIfExists(string name, IReadOnlyList<FirewallRuleInfo> existing)
    {
        var namePrefix = FirewallRuleNames.GetRuleNamePrefix(name);
        var existingRule = existing.FirstOrDefault(r =>
            r.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase));
        if (existingRule == null)
            return;

        ruleManager.RemoveRule(existingRule.Name);
    }

    private static FirewallRuleInfo BuildRuleInfo(string name, string sid, string remoteAddress) =>
        new(
            Name: name,
            LocalUser: FirewallSddlHelper.BuildSddl(sid),
            RemoteAddress: remoteAddress,
            Direction: DirectionOutbound,
            Action: ActionBlock,
            Protocol: ProtocolAny,
            Grouping: FirewallConstants.RuleGrouping,
            Description: "Managed by RunFence");
}

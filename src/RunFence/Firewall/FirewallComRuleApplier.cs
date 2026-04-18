using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Manages COM-based firewall rules: internet block, LAN block, and local address rules.
/// Handles add/update/remove of <see cref="FirewallRuleInfo"/> entries and rollback on failure.
/// </summary>
public class FirewallComRuleApplier(
    IFirewallRuleManager ruleManager,
    FirewallAddressExclusionBuilder addressBuilder,
    ILoggingService log)
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
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        if (settings is { AllowInternet: true, AllowLan: true })
            return false;

        var existing = GetExistingRulesBySid(sid);
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

    public bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings)
    {
        if (settings.AllowLocalhost)
            return false;

        var existing = GetExistingRulesBySid(sid);
        var addresses = addressBuilder.ComputeLocalAddressRanges();

        var ipv4Name = FirewallRuleNames.LocalAddressIPv4RuleName(username);
        var ipv6Name = FirewallRuleNames.LocalAddressIPv6RuleName(username);
        var ipv4Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv4Name);
        var ipv6Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv6Name);

        var ipv4Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase));
        var ipv6Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase));

        if (IsRuleInDesiredState(ipv4Rule, ipv4Name, sid, addresses.IPv4Address) &&
            IsRuleInDesiredState(ipv6Rule, ipv6Name, sid, addresses.IPv6Address))
            return false;

        EnsureRule(ipv4Name, sid, addresses.IPv4Address, existing);
        EnsureRule(ipv6Name, sid, addresses.IPv6Address, existing);
        return true;
    }

    public IReadOnlyList<FirewallRuleInfo> GetExistingRulesBySid(string sid)
    {
        var allRules = ruleManager.GetRulesByGroup(FirewallConstants.RuleGrouping);
        return allRules
            .Where(r => string.Equals(FirewallSddlHelper.ExtractSid(r.LocalUser), sid, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void RollBackAccountRules(
        string sid,
        FirewallAccountSettings rollbackSettings,
        IReadOnlyList<FirewallRuleInfo> capturedRules)
    {
        RestoreWindowsFirewallRules(sid, capturedRules);
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
        var ipv4Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv4Name);
        var ipv6Prefix = FirewallRuleNames.GetRuleNamePrefix(ipv6Name);
        var ipv4Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase));
        var ipv6Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase));

        bool changed = RefreshSingleRule(sid, ipv4Name, addresses.IPv4Address, ipv4Rule);
        changed |= RefreshSingleRule(sid, ipv6Name, addresses.IPv6Address, ipv6Rule);
        return changed;
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

    private void RestoreWindowsFirewallRules(string sid, IReadOnlyList<FirewallRuleInfo> capturedRules)
    {
        IReadOnlyList<FirewallRuleInfo> currentRules;
        try
        {
            currentRules = GetExistingRulesBySid(sid);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallComRuleApplier: Failed to enumerate firewall rules for rollback of SID '{sid}'", ex);
            return;
        }

        var capturedByName = RulesByName(capturedRules);
        foreach (var currentRule in currentRules)
        {
            if (capturedByName.ContainsKey(currentRule.Name))
                continue;

            try
            {
                ruleManager.RemoveRule(currentRule.Name);
            }
            catch (Exception ex)
            {
                log.Error($"FirewallComRuleApplier: Failed to remove newly-created rollback rule '{currentRule.Name}'", ex);
            }
        }

        IReadOnlyList<FirewallRuleInfo> postRemovalRules;
        try
        {
            postRemovalRules = GetExistingRulesBySid(sid);
        }
        catch (Exception ex)
        {
            log.Error($"FirewallComRuleApplier: Failed to re-enumerate firewall rules for rollback of SID '{sid}'", ex);
            postRemovalRules = currentRules.Where(r => capturedByName.ContainsKey(r.Name)).ToList();
        }

        var currentByName = RulesByName(postRemovalRules);
        foreach (var capturedRule in capturedRules)
        {
            if (!currentByName.TryGetValue(capturedRule.Name, out var currentRule))
            {
                try
                {
                    ruleManager.AddRule(capturedRule);
                }
                catch (Exception ex)
                {
                    log.Error($"FirewallComRuleApplier: Failed to restore missing rollback rule '{capturedRule.Name}'", ex);
                }

                continue;
            }

            if (currentRule == capturedRule)
                continue;

            try
            {
                ruleManager.UpdateRule(currentRule.Name, capturedRule);
            }
            catch (Exception ex)
            {
                log.Error($"FirewallComRuleApplier: Failed to restore rollback rule '{capturedRule.Name}'", ex);
            }
        }
    }

    private static Dictionary<string, FirewallRuleInfo> RulesByName(IReadOnlyList<FirewallRuleInfo> rules)
    {
        var byName = new Dictionary<string, FirewallRuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
            byName.TryAdd(rule.Name, rule);
        return byName;
    }

    private static bool IsRuleInDesiredState(
        FirewallRuleInfo? existingRule,
        string desiredName,
        string sid,
        string desiredRemoteAddress)
    {
        if (string.IsNullOrEmpty(desiredRemoteAddress))
            return existingRule == null;

        return existingRule == BuildRuleInfo(desiredName, sid, desiredRemoteAddress);
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

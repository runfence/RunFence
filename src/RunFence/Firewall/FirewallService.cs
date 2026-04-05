using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.Wfp;

namespace RunFence.Firewall;

public class FirewallService : IFirewallService
{
    private readonly ILoggingService _log;
    private readonly IFirewallRuleManager _ruleManager;
    private readonly IDnsResolver _dnsResolver;
    private readonly INetworkInterfaceInfoProvider _networkInfo;
    private readonly IWfpLocalhostBlocker _wfpBlocker;
    private readonly FirewallAddressRangeBuilder _rangeBuilder;

    private const string RuleGrouping = "RunFence";
    private const int DirectionOutbound = 2;
    private const int ActionBlock = 0;
    private const int ProtocolAny = 256;

    private static readonly Regex SddlSidPattern = new(@"D:\(A;;CC;;;([^)]+)\)", RegexOptions.Compiled);

    public FirewallService(ILoggingService log, FirewallAddressRangeBuilder rangeBuilder,
        IFirewallRuleManager ruleManager, IDnsResolver dnsResolver,
        INetworkInterfaceInfoProvider networkInfo, IWfpLocalhostBlocker wfpBlocker)
    {
        _log = log;
        _rangeBuilder = rangeBuilder;
        _ruleManager = ruleManager;
        _wfpBlocker = wfpBlocker;
        _dnsResolver = dnsResolver;
        _networkInfo = networkInfo;
    }

    public void ApplyFirewallRules(string sid, string username, FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains = null)
    {
        try
        {
            var existing = GetExistingRulesBySid(sid);
            ApplyInternetRules(sid, username, settings, existing, preResolvedDomains);
            ApplyLocalhostRules(sid, username, settings, existing);
            ApplyLanRules(sid, username, settings, existing, preResolvedDomains);
        }
        catch (Exception ex)
        {
            _log.Error($"FirewallService: Failed to apply rules for {sid}", ex);
        }
    }

    public void RemoveAllRules(string sid)
    {
        try
        {
            var toRemove = GetExistingRulesBySid(sid);
            foreach (var rule in toRemove)
            {
                try
                {
                    _ruleManager.RemoveRule(rule.Name);
                }
                catch (Exception ex)
                {
                    _log.Error($"FirewallService: Failed to remove rule '{rule.Name}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"FirewallService: Failed to remove rules for {sid}", ex);
        }

        _wfpBlocker.Apply(sid, block: false);
    }

    public bool RefreshAllowlistRules(string sid, string username, FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains = null)
    {
        if (settings is { AllowInternet: true, AllowLan: true })
            return false;
        try
        {
            var existing = GetExistingRulesBySid(sid);
            bool changed = false;

            if (!settings.AllowInternet)
                changed |= RefreshRulePair(
                    InternetIPv4RuleName(username), InternetIPv6RuleName(username),
                    ComputeInternetAddresses(settings, preResolvedDomains), existing);

            if (!settings.AllowLan)
                changed |= RefreshRulePair(
                    LanIPv4RuleName(username), LanIPv6RuleName(username),
                    ComputeLanAddresses(settings, preResolvedDomains), existing);

            return changed;
        }
        catch (Exception ex)
        {
            _log.Error($"FirewallService: Failed to refresh allowlist rules for {sid}", ex);
            return false;
        }
    }

    public bool RefreshLocalAddressRules(string sid, string username, FirewallAccountSettings settings)
    {
        if (settings.AllowLocalhost)
            return false;
        try
        {
            var existing = GetExistingRulesBySid(sid);
            var (ipv4Addresses, ipv6Addresses) = ComputeLocalAddressRanges();

            var ipv4Name = LocalAddressIPv4RuleName(username);
            var ipv6Name = LocalAddressIPv6RuleName(username);
            var ipv4Prefix = GetRuleNamePrefix(ipv4Name);
            var ipv6Prefix = GetRuleNamePrefix(ipv6Name);

            var oldIpv4 = existing.FirstOrDefault(r => r.Name.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase))?.RemoteAddress ?? "";
            var oldIpv6 = existing.FirstOrDefault(r => r.Name.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase))?.RemoteAddress ?? "";

            if (oldIpv4 == ipv4Addresses && oldIpv6 == ipv6Addresses)
                return false;

            // EnsureRule handles both add-if-missing and update-in-place.
            EnsureRule(ipv4Name, sid, ipv4Addresses, existing);
            EnsureRule(ipv6Name, sid, ipv6Addresses, existing);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"FirewallService: Failed to refresh local address rules for {sid}", ex);
            return false;
        }
    }

    private bool RefreshRulePair(string ipv4Name, string ipv6Name,
        (string IPv4Address, string IPv6Address) addresses,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        var (newIpv4Address, newIpv6Address) = addresses;
        var ipv4Prefix = GetRuleNamePrefix(ipv4Name);
        var ipv6Prefix = GetRuleNamePrefix(ipv6Name);
        var ipv4Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv4Prefix, StringComparison.OrdinalIgnoreCase));
        var ipv6Rule = existing.FirstOrDefault(r => r.Name.StartsWith(ipv6Prefix, StringComparison.OrdinalIgnoreCase));

        bool changed = RefreshSingleRule(ipv4Name, newIpv4Address, ipv4Rule);
        changed |= RefreshSingleRule(ipv6Name, newIpv6Address, ipv6Rule);
        return changed;
    }

    private bool RefreshSingleRule(string targetName, string newAddress, FirewallRuleInfo? existingRule)
    {
        if (existingRule == null || existingRule.RemoteAddress == newAddress)
            return false;
        if (string.IsNullOrEmpty(newAddress))
        {
            try
            {
                _ruleManager.RemoveRule(existingRule.Name);
            }
            catch (Exception ex)
            {
                _log.Error($"FirewallService: Failed to remove rule '{existingRule.Name}'", ex);
            }
        }
        else
            _ruleManager.UpdateRule(existingRule.Name, existingRule with { Name = targetName, RemoteAddress = newAddress });

        return true;
    }

    public void EnforceAll(AppDatabase database)
    {
        _log.Info("FirewallService: enforcing all rules.");
        IReadOnlyList<FirewallRuleInfo> allRules;
        try
        {
            allRules = _ruleManager.GetRulesByGroup(RuleGrouping);
        }
        catch (Exception ex)
        {
            _log.Warn($"FirewallService: Windows Firewall unavailable, skipping enforcement: {ex.Message}");
            return;
        }

        var activeSids = new HashSet<string>(
            database.Accounts.Where(a => !a.Firewall.IsDefault).Select(a => a.Sid),
            StringComparer.OrdinalIgnoreCase);

        foreach (var account in database.Accounts.Where(a => !a.Firewall.IsDefault))
        {
            var username = database.SidNames.TryGetValue(account.Sid, out var name) ? name : account.Sid;
            ApplyFirewallRules(account.Sid, username, account.Firewall);
        }

        CleanupOrphanedRulesFromList(allRules, activeSids);
        _log.Info($"FirewallService: enforcement complete ({activeSids.Count} account(s)).");
    }

    private void ApplyInternetRules(string sid, string username,
        FirewallAccountSettings settings, IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains)
        => ApplyOrRemoveRulePair(sid, InternetIPv4RuleName(username), InternetIPv6RuleName(username),
            !settings.AllowInternet, () => ComputeInternetAddresses(settings, preResolvedDomains), existing);

    private void ApplyLocalhostRules(string sid, string username,
        FirewallAccountSettings settings, IReadOnlyList<FirewallRuleInfo> existing)
    {
        // INetFwRule outbound rules do not apply to loopback traffic because Windows Firewall
        // implicitly excludes the loopback interface from its WFP filters. Use direct WFP
        // filters via IWfpLocalhostBlocker, which do not carry this implicit exclusion.
        _wfpBlocker.Apply(sid, !settings.AllowLocalhost);

        // Remove any legacy INetFwRule localhost rules that may exist from prior versions.
        RemoveRuleIfExists(LocalhostIPv4RuleName(username), existing);
        RemoveRuleIfExists(LocalhostIPv6RuleName(username), existing);

        // When localhost is blocked, also block the machine's own non-loopback IPs (LAN, VPN
        // adapters, etc.) — these are alternate self-connection paths not covered by WFP loopback
        // filters, and may fall outside the internet/LAN block ranges when those aren't blocked.
        ApplyOrRemoveRulePair(sid, LocalAddressIPv4RuleName(username), LocalAddressIPv6RuleName(username),
            !settings.AllowLocalhost, ComputeLocalAddressRanges, existing);
    }

    private (string IPv4Addresses, string IPv6Addresses) ComputeLocalAddressRanges()
    {
        var localAddresses = _networkInfo.GetLocalAddresses();
        var ipv4 = string.Join(",", localAddresses
            .Where(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork));
        var ipv6 = string.Join(",", localAddresses
            .Where(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6));
        return (ipv4, ipv6);
    }

    private void ApplyLanRules(string sid, string username,
        FirewallAccountSettings settings, IReadOnlyList<FirewallRuleInfo> existing,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains)
        => ApplyOrRemoveRulePair(sid, LanIPv4RuleName(username), LanIPv6RuleName(username),
            !settings.AllowLan, () => ComputeLanAddresses(settings, preResolvedDomains), existing);

    private void ApplyOrRemoveRulePair(string sid, string ipv4Name, string ipv6Name, bool shouldBlock,
        Func<(string, string)> computeAddresses, IReadOnlyList<FirewallRuleInfo> existing)
    {
        if (shouldBlock)
        {
            var (ipv4Address, ipv6Address) = computeAddresses();
            EnsureRule(ipv4Name, sid, ipv4Address, existing);
            EnsureRule(ipv6Name, sid, ipv6Address, existing);
        }
        else
        {
            RemoveRuleIfExists(ipv4Name, existing);
            RemoveRuleIfExists(ipv6Name, existing);
        }
    }

    private (string IPv4Address, string IPv6Address) ComputeInternetAddresses(
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains)
    {
        var allExclusions = ComputeAllowlistExclusions(settings, preResolvedDomains);
        return (
            _rangeBuilder.BuildInternetIPv4Range(allExclusions),
            _rangeBuilder.BuildInternetIPv6Range(allExclusions));
    }

    private (string IPv4Address, string IPv6Address) ComputeLanAddresses(
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains)
    {
        if (settings.Allowlist.Count == 0 && !settings.AllowLocalhost)
            return (_rangeBuilder.BuildLanIPv4Range(), _rangeBuilder.BuildLanIPv6Range());
        var allExclusions = ComputeAllowlistExclusions(settings, preResolvedDomains);
        return (
            _rangeBuilder.BuildLanIPv4Range(allExclusions),
            _rangeBuilder.BuildLanIPv6Range(allExclusions));
    }

    private List<string> ComputeAllowlistExclusions(
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolvedDomains)
    {
        var allExclusions = new List<string>();
        bool hasAllowlistEntries = settings.Allowlist.Count > 0;

        if (hasAllowlistEntries)
            allExclusions.AddRange(_networkInfo.GetDnsServerAddresses());

        if (settings.AllowLocalhost)
            allExclusions.AddRange(_networkInfo.GetLocalAddresses());

        foreach (var entry in settings.Allowlist)
        {
            if (!entry.IsDomain)
                allExclusions.Add(entry.Value);
            else if (preResolvedDomains != null && preResolvedDomains.TryGetValue(entry.Value, out var preResolved))
                allExclusions.AddRange(preResolved);
            else
            {
                // Resolve with timeout (called on UI thread or with no pre-resolved data)
                var resolveTask = _dnsResolver.ResolveAsync(entry.Value);
                if (!resolveTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _log.Warn($"DNS resolution timed out for '{entry.Value}'");
                    continue;
                }

                allExclusions.AddRange(resolveTask.Result);
            }
        }

        return allExclusions;
    }

    private void EnsureRule(string name, string sid, string remoteAddress,
        IReadOnlyList<FirewallRuleInfo> existing)
    {
        // Match by name prefix (rule type) so stale rules from account renames are replaced.
        // The prefix is the part of the name up to and including the opening parenthesis.
        var namePrefix = GetRuleNamePrefix(name);
        var existingRule = existing.FirstOrDefault(r =>
            r.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase));

        // If exclusions consumed all ranges, the remote address is empty. An empty RemoteAddress
        // is invalid for Windows Firewall rules, so remove the rule instead of creating/updating it.
        if (string.IsNullOrEmpty(remoteAddress))
        {
            if (existingRule != null)
                RemoveRuleIfExists(name, existing);
            return;
        }

        var info = new FirewallRuleInfo(
            Name: name,
            LocalUser: $"D:(A;;CC;;;{sid})",
            RemoteAddress: remoteAddress,
            Direction: DirectionOutbound,
            Action: ActionBlock,
            Protocol: ProtocolAny,
            Grouping: RuleGrouping,
            Description: "Managed by RunFence");

        if (existingRule == null)
            _ruleManager.AddRule(info);
        else if (!string.Equals(existingRule.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 existingRule.RemoteAddress != remoteAddress ||
                 existingRule.LocalUser != info.LocalUser)
            _ruleManager.UpdateRule(existingRule.Name, info);
    }

    private static string GetRuleNamePrefix(string ruleName)
    {
        // Find the first '(' which separates the rule type from the username.
        // Using IndexOf (not LastIndexOf) so usernames containing '(' are handled correctly.
        var parenIndex = ruleName.IndexOf('(');
        return parenIndex >= 0 ? ruleName[..(parenIndex + 1)] : ruleName;
    }

    private void RemoveRuleIfExists(string name, IReadOnlyList<FirewallRuleInfo> existing)
    {
        var namePrefix = GetRuleNamePrefix(name);
        var existingRule = existing.FirstOrDefault(r =>
            r.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase));
        if (existingRule != null)
        {
            try
            {
                _ruleManager.RemoveRule(existingRule.Name);
            }
            catch (Exception ex)
            {
                _log.Error($"FirewallService: Failed to remove rule '{existingRule.Name}'", ex);
            }
        }
    }

    private IReadOnlyList<FirewallRuleInfo> GetExistingRulesBySid(string sid)
    {
        var allRules = _ruleManager.GetRulesByGroup(RuleGrouping);
        return allRules.Where(r => string.Equals(ExtractSidFromSddl(r.LocalUser), sid, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void CleanupOrphanedRulesFromList(IReadOnlyList<FirewallRuleInfo> allRules, HashSet<string> activeSids)
    {
        foreach (var rule in allRules)
        {
            var ruleSid = ExtractSidFromSddl(rule.LocalUser);
            if (ruleSid == null || activeSids.Contains(ruleSid))
                continue;
            try
            {
                _ruleManager.RemoveRule(rule.Name);
            }
            catch (Exception ex)
            {
                _log.Error($"FirewallService: Failed to remove orphaned rule '{rule.Name}'", ex);
            }

            try
            {
                _wfpBlocker.Apply(ruleSid, block: false);
            }
            catch (Exception ex)
            {
                _log.Error($"FirewallService: Failed to remove orphaned WFP filter for SID '{ruleSid}'", ex);
            }
        }
    }

    private static string? ExtractSidFromSddl(string sddl)
    {
        var m = SddlSidPattern.Match(sddl);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string InternetIPv4RuleName(string username) => $"RunFence Block Internet IPv4 ({username})";
    private static string InternetIPv6RuleName(string username) => $"RunFence Block Internet IPv6 ({username})";
    private static string LocalhostIPv4RuleName(string username) => $"RunFence Block Localhost IPv4 ({username})";
    private static string LocalhostIPv6RuleName(string username) => $"RunFence Block Localhost IPv6 ({username})";
    private static string LocalAddressIPv4RuleName(string username) => $"RunFence Block Local Addresses IPv4 ({username})";
    private static string LocalAddressIPv6RuleName(string username) => $"RunFence Block Local Addresses IPv6 ({username})";
    private static string LanIPv4RuleName(string username) => $"RunFence Block LAN IPv4 ({username})";
    private static string LanIPv6RuleName(string username) => $"RunFence Block LAN IPv6 ({username})";
}
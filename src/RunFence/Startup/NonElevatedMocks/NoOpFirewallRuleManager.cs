#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using RunFence.Firewall;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpFirewallRuleManager(IFirewallRuleManager real) : IFirewallRuleManager
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public IReadOnlyList<FirewallRuleInfo> GetRulesByGroup(string grouping) => [];
    public void AddRule(FirewallRuleInfo rule) { }
    public void UpdateRule(string existingName, FirewallRuleInfo rule) { }
    public void RemoveRule(string name) { }
}

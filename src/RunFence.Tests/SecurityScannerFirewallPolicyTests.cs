using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerFirewallPolicyTests : SecurityScannerTestBase
{
    // --- Windows Firewall ---

    [Fact]
    public void FirewallServiceDisabled_ReportsSingleFinding_SkipsProfileCheck()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: true, IsStopped: true));
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("disabled", firewallFindings[0].TargetDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("services.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallServiceStopped_NotDisabled_ReportsSingleFinding_SkipsProfileCheck()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: true));
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("not running", firewallFindings[0].TargetDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("services.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallServiceRunning_ProfileDisabled_ReportsProfileFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: false));
            s.SetFirewallProfileStates([
                ("Domain", true),
                ("Private", false),
                ("Public", false),
            ]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Equal(2, firewallFindings.Count);
        Assert.Contains(firewallFindings, f => f.TargetDescription.Contains("Private"));
        Assert.Contains(firewallFindings, f => f.TargetDescription.Contains("Public"));
        Assert.All(firewallFindings, f => Assert.Equal("wf.msc", f.NavigationTarget));
    }

    [Fact]
    public void FirewallServiceRunning_AllProfilesEnabled_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: false));
            s.SetFirewallProfileStates([("Domain", true), ("Private", true), ("Public", true)]);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.FirewallPolicy);
    }

    [Fact]
    public void FirewallServiceStateUnavailable_ProfileDisabled_ReportsProfileFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState(null);
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("Public", firewallFindings[0].TargetDescription);
        Assert.Equal("wf.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallBothUnavailable_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState(null);
            s.SetFirewallProfileStates(null);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.FirewallPolicy);
    }

}

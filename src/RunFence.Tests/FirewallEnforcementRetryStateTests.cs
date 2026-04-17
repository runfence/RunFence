using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallEnforcementRetryStateTests
{
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private const string DeletedSid = "S-1-5-21-2000-2000-2000-2002";

    [Fact]
    public void UpdateDnsServersAndReturnChanged_TracksServerSetChanges()
    {
        var state = new FirewallEnforcementRetryState();

        Assert.True(state.UpdateDnsServersAndReturnChanged(["192.0.2.53", "198.51.100.53"]));
        Assert.False(state.UpdateDnsServersAndReturnChanged(["198.51.100.53", "192.0.2.53"]));
        Assert.True(state.UpdateDnsServersAndReturnChanged(["203.0.113.53"]));
    }

    [Fact]
    public void UpdateDnsServersAndReturnChanged_WhenDuplicateMasksRemovedServer_ReturnsChanged()
    {
        var state = new FirewallEnforcementRetryState();

        state.UpdateDnsServersAndReturnChanged(["192.0.2.53", "198.51.100.53"]);
        bool changed = state.UpdateDnsServersAndReturnChanged(["192.0.2.53", "192.0.2.53"]);

        Assert.True(changed);
    }

    [Fact]
    public void DnsServerRefreshPendingSids_ClearOnSuccessAndPruneDeletedAccounts()
    {
        var state = new FirewallEnforcementRetryState();
        state.MarkDnsServerRefreshPending([Sid, DeletedSid]);

        state.MarkDnsServerRefreshSucceeded(Sid);
        state.Prune(new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = Sid }]
        });

        Assert.Empty(state.GetDnsServerRefreshPendingSids());
    }

    [Fact]
    public void GlobalIcmpDirtyState_ClearsOnlyOnSucceededOrClear()
    {
        var state = new FirewallEnforcementRetryState();

        state.MarkGlobalIcmpDirty();
        Assert.True(state.IsGlobalIcmpDirty());

        state.MarkGlobalIcmpSucceeded();
        Assert.False(state.IsGlobalIcmpDirty());

        state.MarkGlobalIcmpDirty();
        state.Clear();
        Assert.False(state.IsGlobalIcmpDirty());
    }
}

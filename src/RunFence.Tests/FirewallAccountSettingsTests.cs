using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class FirewallAccountSettingsTests
{
    [Fact]
    public void Clone_DeepCopiesAllowlistEntries()
    {
        var settings = new FirewallAccountSettings
        {
            Allowlist =
            [
                new FirewallAllowlistEntry
                {
                    Value = "example.com",
                    IsDomain = true
                }
            ]
        };

        var clone = settings.Clone();
        settings.Allowlist[0].Value = "changed.example";
        settings.Allowlist[0].IsDomain = false;

        Assert.NotSame(settings.Allowlist[0], clone.Allowlist[0]);
        Assert.Equal("example.com", clone.Allowlist[0].Value);
        Assert.True(clone.Allowlist[0].IsDomain);
    }

    [Fact]
    public void Clone_PreservesAllScalarFields()
    {
        var settings = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = false,
            AllowLan = false,
            FilterEphemeralLoopback = false
        };

        var clone = settings.Clone();

        Assert.False(clone.AllowInternet);
        Assert.False(clone.AllowLocalhost);
        Assert.False(clone.AllowLan);
        Assert.False(clone.FilterEphemeralLoopback);
    }

    [Fact]
    public void Clone_EmptyAllowlist_ProducesEmptyAllowlistInClone()
    {
        var settings = new FirewallAccountSettings { Allowlist = [] };

        var clone = settings.Clone();

        Assert.NotNull(clone.Allowlist);
        Assert.Empty(clone.Allowlist);
        Assert.NotSame(settings.Allowlist, clone.Allowlist);
    }

    [Fact]
    public void Clone_DeepCopiesLocalhostPortExemptions()
    {
        var settings = new FirewallAccountSettings
        {
            LocalhostPortExemptions = ["53", "8080", "3000-3010"]
        };

        var clone = settings.Clone();

        // Mutate the original list — clone must be unaffected
        settings.LocalhostPortExemptions[0] = "changed";
        settings.LocalhostPortExemptions.Add("99999");

        Assert.NotSame(settings.LocalhostPortExemptions, clone.LocalhostPortExemptions);
        Assert.Equal(3, clone.LocalhostPortExemptions.Count);
        Assert.Equal("53", clone.LocalhostPortExemptions[0]);
        Assert.Equal("8080", clone.LocalhostPortExemptions[1]);
        Assert.Equal("3000-3010", clone.LocalhostPortExemptions[2]);
    }
}

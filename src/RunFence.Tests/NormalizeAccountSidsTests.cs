using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for AppInitializationHelper.NormalizeAccountSids — the startup fix that sets empty AccountSid
/// to the current account SID, with the AppContainer exception.
/// </summary>
public class NormalizeAccountSidsTests
{
    private const string CurrentSid = "S-1-5-21-1111111111-2222222222-3333333333-1000";

    private static AppInitializationHelper CreateHelper() =>
        new(new Mock<ISidResolver>().Object);

    [Fact]
    public void NormalizeAccountSids_EmptySid_NoContainer_SetsToCurrent()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "App", ExePath = @"C:\app.exe", AccountSid = "" }
        };

        var changed = CreateHelper().NormalizeAccountSids(apps, CurrentSid);

        Assert.True(changed);
        Assert.Equal(CurrentSid, apps[0].AccountSid);
    }

    [Fact]
    public void NormalizeAccountSids_NonEmptySid_NotChanged()
    {
        var existingSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
        var apps = new List<AppEntry>
        {
            new() { Name = "App", ExePath = @"C:\app.exe", AccountSid = existingSid }
        };

        var changed = CreateHelper().NormalizeAccountSids(apps, CurrentSid);

        Assert.False(changed);
        Assert.Equal(existingSid, apps[0].AccountSid);
    }

    [Fact]
    public void NormalizeAccountSids_SkipsAppContainerEntries()
    {
        // AppContainer apps have empty AccountSid by design — must NOT be overwritten
        var apps = new List<AppEntry>
        {
            new() { Name = "SandboxedApp", ExePath = @"C:\browser.exe", AccountSid = "", AppContainerName = "ram_browser" }
        };

        var changed = CreateHelper().NormalizeAccountSids(apps, CurrentSid);

        Assert.False(changed);
        Assert.Equal("", apps[0].AccountSid); // Must stay empty
        Assert.Equal("ram_browser", apps[0].AppContainerName);
    }

    [Fact]
    public void NormalizeAccountSids_MixedApps_OnlyNormalizesNonContainers()
    {
        var apps = new List<AppEntry>
        {
            new() { Name = "RegularApp", ExePath = @"C:\app.exe", AccountSid = "" },
            new() { Name = "ContainerApp", ExePath = @"C:\browser.exe", AccountSid = "", AppContainerName = "ram_browser" }
        };

        var changed = CreateHelper().NormalizeAccountSids(apps, CurrentSid);

        Assert.True(changed);
        Assert.Equal(CurrentSid, apps[0].AccountSid); // Normalized
        Assert.Equal("", apps[1].AccountSid); // NOT normalized
    }

    [Fact]
    public void NormalizeAccountSids_EmptyList_ReturnsFalse()
    {
        var changed = CreateHelper().NormalizeAccountSids(new List<AppEntry>(), CurrentSid);

        Assert.False(changed);
    }
}
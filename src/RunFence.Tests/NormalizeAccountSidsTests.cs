using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for AppInitializationHelper — NormalizeAccountSids and InitializeNewDatabase.
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
    public void InitializeNewDatabase_SystemAccount_HasHighestAllowed()
    {
        var database = new AppDatabase();

        CreateHelper().InitializeNewDatabase(database);

        var system = database.GetAccount(SidConstants.SystemSid);
        Assert.NotNull(system);
        Assert.Equal(PrivilegeLevel.HighestAllowed, system.PrivilegeLevel);
    }
}
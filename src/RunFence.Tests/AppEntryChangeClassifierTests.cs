using RunFence.Apps.UI;
using RunFence.Apps;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppEntryChangeClassifierTests
{
    [Fact]
    public void Classify_PrivilegeOnlyEdit_RequiresOnlyCurrentConfigSave()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();
        newApp.PrivilegeLevel = PrivilegeLevel.Basic;

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.False(result.RequiresAclReapply);
        Assert.False(result.RequiresBesideTargetRefresh);
        Assert.False(result.RequiresHandlerSync);
        Assert.False(result.RequiresManagedShortcutRefresh);
        Assert.False(result.RequiresIconRefresh);
        Assert.Equal(AppEditConfigSaveScope.CurrentAppConfigOnly, result.ConfigSaveScope);
    }

    [Fact]
    public void Classify_HandlerOnlyEdit_RequiresHandlerSyncOnly()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [new HandlerAssociationItem(".txt", null, null, false)],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.False(result.RequiresAclReapply);
        Assert.False(result.RequiresBesideTargetRefresh);
        Assert.True(result.RequiresHandlerSync);
        Assert.False(result.RequiresManagedShortcutRefresh);
        Assert.False(result.RequiresIconRefresh);
        Assert.Equal(AppEditConfigSaveScope.CurrentAppConfigOnly, result.ConfigSaveScope);
    }

    [Fact]
    public void Classify_ConfigOwnershipMove_RequiresAllConfigsSave()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: @"C:\configs\extra.rfn");

        Assert.Equal(AppEditConfigSaveScope.AllConfigs, result.ConfigSaveScope);
    }

    [Fact]
    public void Classify_FilePathChange_RefreshesAclBesideTargetAndIconWithoutManagedShortcutRefresh()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();
        newApp.ExePath = @"C:\Apps\Updated\App.exe";

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.True(result.RequiresAclReapply);
        Assert.True(result.RequiresBesideTargetRefresh);
        Assert.False(result.RequiresHandlerSync);
        Assert.False(result.RequiresManagedShortcutRefresh);
        Assert.True(result.RequiresIconRefresh);
    }

    [Fact]
    public void Classify_NameChange_RefreshesBesideTargetAndManagedShortcuts()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.False(result.RequiresAclReapply);
        Assert.True(result.RequiresBesideTargetRefresh);
        Assert.False(result.RequiresHandlerSync);
        Assert.True(result.RequiresManagedShortcutRefresh);
        Assert.True(result.RequiresIconRefresh);
    }

    [Fact]
    public void Classify_ManageShortcutsToggleForFileApp_DoesNotRequireIconRefresh()
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();
        newApp.ManageShortcuts = !previousApp.ManageShortcuts;

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.False(result.RequiresAclReapply);
        Assert.False(result.RequiresBesideTargetRefresh);
        Assert.False(result.RequiresHandlerSync);
        Assert.True(result.RequiresManagedShortcutRefresh);
        Assert.False(result.RequiresIconRefresh);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Classify_LaunchArgumentOrWorkingDirectoryPassingToggle_RequiresManagedShortcutRefresh(
        bool allowPassingArguments,
        bool allowPassingWorkingDirectory)
    {
        var previousApp = CreateApp();
        var newApp = previousApp.Clone();
        newApp.AllowPassingArguments = allowPassingArguments;
        newApp.AllowPassingWorkingDirectory = allowPassingWorkingDirectory;

        var result = new AppEntryChangeClassifier().Classify(
            previousApp,
            newApp,
            [],
            [],
            previousConfigPath: null,
            newConfigPath: null);

        Assert.True(result.RequiresManagedShortcutRefresh);
    }

    private static AppEntry CreateApp() => new()
    {
        Id = "app01",
        Name = "App",
        ExePath = @"C:\Apps\App.exe",
        AccountSid = "S-1-5-21-1-2-3-1001",
        RestrictAcl = true,
        AclMode = AclMode.Deny,
        AclTarget = AclTarget.File,
        DeniedRights = DeniedRights.Execute,
        ManageShortcuts = false
    };
}

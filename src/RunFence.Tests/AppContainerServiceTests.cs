using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class AppContainerServiceTests
{
    private static AppContainerService CreateService()
    {
        var log = new Mock<ILoggingService>();
        var pathGrantService = new Mock<IPathGrantService>();
        var environmentSetup = new Mock<IAppContainerEnvironmentSetup>();
        var profileSetup = new AppContainerProfileSetup(log.Object, environmentSetup.Object);
        var dataFolderService = new AppContainerDataFolderService(log.Object, pathGrantService.Object);
        var explorerTokenProvider = new Mock<IExplorerTokenProvider>();
        var sidProvider = new AppContainerSidProvider();
        return new AppContainerService(log.Object, pathGrantService.Object, profileSetup, dataFolderService,
            () => new AppContainerComAccessService(log.Object), explorerTokenProvider.Object, sidProvider);
    }

    [Fact]
    public void GetContainersRootPath_ReturnsAcUnderProgramData()
    {
        var result = AppContainerPaths.GetContainersRootPath();

        Assert.Equal(Path.Combine(Constants.ProgramDataDir, "AC"), result);
    }

    [Theory]
    [InlineData("ram_browser")]
    [InlineData("ram_sandbox")]
    public void GetContainerDataPath_ContainsProfileNameUnderAcRoot(string profileName)
    {
        var result = AppContainerPaths.GetContainerDataPath(profileName);

        Assert.EndsWith(@"\AC\" + profileName, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetContainerDataPath_DifferentProfileNames_ReturnDistinctPaths()
    {
        var path1 = AppContainerPaths.GetContainerDataPath("ram_browser");
        var path2 = AppContainerPaths.GetContainerDataPath("ram_sandbox");

        Assert.NotEqual(path1, path2);
    }

    // --- GetSid ---

    [Fact]
    public void GetSid_ValidName_ReturnsAppContainerSidFormat()
    {
        // DeriveAppContainerSidFromAppContainerName is a pure Windows computation —
        // no profile needs to exist on disk. Result is always an AppContainer SID (S-1-15-2-...).
        var sid = CreateService().GetSid("ram_test_container");

        Assert.NotEmpty(sid);
        // AppContainer SIDs have integrity level 15 (Low IL is 12; AC SIDs use authority 15)
        Assert.StartsWith("S-1-15-2-", sid);
    }

    [Theory]
    [InlineData("ram_browser")]
    [InlineData("ram_sandbox")]
    public void GetSid_SameName_ReturnsDeterministicSid(string name)
    {
        var service = CreateService();

        var sid1 = service.GetSid(name);
        var sid2 = service.GetSid(name);

        Assert.Equal(sid1, sid2);
    }

    [Fact]
    public void GetSid_DifferentNames_ReturnDistinctSids()
    {
        var service = CreateService();

        var sid1 = service.GetSid("ram_container_alpha");
        var sid2 = service.GetSid("ram_container_beta");

        Assert.NotEqual(sid1, sid2);
    }

    // --- ProfileExists failure path ---

    [Fact]
    public void ProfileExists_UnknownContainer_ReturnsFalse()
    {
        // A name with no associated registry mapping returns false (not an exception)
        // Use a name that has never been created on this machine
        var result = CreateService().ProfileExists("ram_nonexistent_profile_xyz_test");

        Assert.False(result);
    }

    // --- GetContainerDataPath delegates to AppContainerPaths ---

    [Fact]
    public void GetContainerDataPath_MatchesStaticHelper()
    {
        var result = CreateService().GetContainerDataPath("ram_browser");

        Assert.Equal(AppContainerPaths.GetContainerDataPath("ram_browser"), result);
    }

    // --- DeleteProfile (non-existent profile) ---

    [Fact]
    public void DeleteProfile_NonExistentProfile_DoesNotThrow()
    {
        // DeleteProfile should log a warning but not throw when the OS profile doesn't exist.
        // DeleteAppContainerProfile returns a non-zero HRESULT for missing profiles, which is logged.
        var service = CreateService();

        var exception = Record.Exception(() => service.DeleteProfile("ram_nonexistent_profile_xyz_test"));

        Assert.Null(exception);
    }

    [Fact]
    public void ProfileExists_NonExistentProfile_ReturnsFalse_AfterDeleteProfile()
    {
        // After deleting a profile that doesn't exist, ProfileExists should return false
        // (same as if it was never created). This verifies that DeleteProfile + ProfileExists
        // are consistent even when the profile was not present.
        var service = CreateService();

        service.DeleteProfile("ram_never_created_xyz_test");
        var exists = service.ProfileExists("ram_never_created_xyz_test");

        Assert.False(exists);
    }
}

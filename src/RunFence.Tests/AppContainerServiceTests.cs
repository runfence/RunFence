using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerServiceTests : IDisposable
{
    private readonly RegistryTestHelper _registry = new("AppContainerServiceHku", "AppContainerServiceHklm");
    private readonly string _containersRootPath = Path.Combine(Path.GetTempPath(), "RunFence_AppContainerService_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _registry.Dispose();
        try
        {
            if (Directory.Exists(_containersRootPath))
                Directory.Delete(_containersRootPath, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void GetContainersRootPath_ReturnsAcUnderProgramData()
    {
        var result = AppContainerPaths.GetContainersRootPath();

        Assert.Equal(Path.Combine(PathConstants.ProgramDataDir, "AC"), result);
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
    public void GetSid_ValidName_ReturnsAppContainerSidFormat()
    {
        var service = CreateService();

        var sid = service.GetSid("ram_test_container");

        Assert.StartsWith("S-1-15-2-", sid);
    }

    [Fact]
    public void GetContainerDataPath_UsesInjectedPathProvider()
    {
        var service = CreateService();

        var path = service.GetContainerDataPath("ram_test");

        Assert.Equal(Path.Combine(_containersRootPath, "ram_test"), path);
    }

    [Fact]
    public void CreateProfile_ProfileSetupFailure_ReturnsFailureAndSkipsDataFolderSetup()
    {
        var profileSetup = new Mock<IAppContainerProfileSetup>();
        var dataFolderService = new Mock<IAppContainerDataFolderService>();
        profileSetup.Setup(s => s.EnsureProfileUnderToken(It.IsAny<AppContainerEntry>(), It.IsAny<IntPtr>()))
            .Returns(AppContainerProfileSetupResult.Failure(
                AppContainerProfileSetupStatus.ProfileFailed,
                "profile failed"));

        var service = CreateService(
            profileSetup: profileSetup.Object,
            dataFolderService: dataFolderService.Object);

        var result = service.CreateProfile(new AppContainerEntry { Name = "ram_test", DisplayName = "Test" });

        Assert.Equal(AppContainerProfileSetupStatus.ProfileFailed, result.Status);
        dataFolderService.Verify(s => s.EnsureContainerDataFolder(It.IsAny<AppContainerEntry>(), It.IsAny<string>()), Times.Never);
        dataFolderService.Verify(s => s.EnsureInteractiveUserAccess(It.IsAny<AppContainerEntry>()), Times.Never);
    }

    [Fact]
    public void ProfileExists_UsesInteractiveUserHiveInsteadOfCurrentUser()
    {
        var interactiveSid = WindowsIdentity.GetCurrent().User!.Value;
        var containerSid = "S-1-15-2-99-1-2-3";

        _registry.HkuRoot.CreateSubKey(
            $@"{interactiveSid}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{containerSid}")
            ?.Dispose();
        _registry.HkuRoot.CreateSubKey(
            $@"S-1-5-21-unrelated\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\other")
            ?.Dispose();

        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_browser")).Returns(containerSid);

        var service = CreateService(sidProvider: sidProvider.Object);

        Assert.True(service.ProfileExists("ram_browser"));
    }

    [Fact]
    public async Task DeleteProfile_RemovesOnlyTargetContainerStateFromLoadedHives()
    {
        const string targetSid = "S-1-15-2-99-9-9";
        const string otherSid = "S-1-15-2-88-8-8";
        const string hiveA = "S-1-5-21-100-100-100-1000";
        const string hiveB = "S-1-5-21-200-200-200-2000";

        CreateContainerState(hiveA, targetSid);
        CreateContainerState(hiveA, otherSid);
        CreateContainerState(hiveB, targetSid);
        CreateContainerState(hiveB, otherSid);

        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_delete")).Returns(targetSid);
        var service = CreateService(sidProvider: sidProvider.Object);

        await service.DeleteProfile("ram_delete");

        Assert.Null(_registry.HkuRoot.OpenSubKey(
            $@"{hiveA}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{targetSid}"));
        Assert.Null(_registry.HkuRoot.OpenSubKey(
            $@"{hiveA}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{targetSid}"));
        Assert.Null(_registry.HkuRoot.OpenSubKey(
            $@"{hiveB}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{targetSid}"));
        Assert.Null(_registry.HkuRoot.OpenSubKey(
            $@"{hiveB}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{targetSid}"));

        Assert.NotNull(_registry.HkuRoot.OpenSubKey(
            $@"{hiveA}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{otherSid}"));
        Assert.NotNull(_registry.HkuRoot.OpenSubKey(
            $@"{hiveB}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{otherSid}"));
    }

    [Fact]
    public async Task DeleteProfile_DeletesInjectedContainerDataPath()
    {
        var path = Path.Combine(_containersRootPath, "ram_delete_data");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "test.txt"), "x");

        var service = CreateService();

        await service.DeleteProfile("ram_delete_data");

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task DeleteProfile_NativeDeleteFailure_PreservesRegistryAndData()
    {
        const string targetSid = "S-1-15-2-99-9-9";
        const string hive = "S-1-5-21-100-100-100-1000";
        var path = Path.Combine(_containersRootPath, "ram_delete_fail");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "test.txt"), "x");
        CreateContainerState(hive, targetSid);

        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_delete_fail")).Returns(targetSid);
        var service = CreateService(sidProvider: sidProvider.Object, deleteProfileHResult: unchecked((int)0x80070005));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteProfile("ram_delete_fail"));

        Assert.Contains("0x80070005", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(_registry.HkuRoot.OpenSubKey(
            $@"{hive}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{targetSid}"));
        Assert.NotNull(_registry.HkuRoot.OpenSubKey(
            $@"{hive}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{targetSid}"));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task DeleteProfile_NativeDeleteFailure_WithMissingLoadedHiveState_PreservesDataFolder()
    {
        const string targetSid = "S-1-15-2-99-7-7";
        var path = Path.Combine(_containersRootPath, "ram_delete_missing_hive");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "test.txt"), "x");

        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_delete_missing_hive")).Returns(targetSid);
        var service = CreateService(sidProvider: sidProvider.Object, deleteProfileHResult: unchecked((int)0x80070005));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteProfile("ram_delete_missing_hive"));

        Assert.Contains("0x80070005", exception.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void RevertTraverseAccess_UsesCachedEntrySidWhenPresent()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var expected = new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true);
        pathGrantService.Setup(p => p.RemoveAll("S-1-15-2-99-cached")).Returns(expected);
        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_cached_sid")).Returns("S-1-15-2-99-override");
        var service = CreateService(pathGrantService: pathGrantService.Object, sidProvider: sidProvider.Object);
        var entry = new AppContainerEntry
        {
            Name = "ram_cached_sid",
            Sid = "S-1-15-2-99-cached"
        };

        var result = service.RevertTraverseAccess(entry, new AppDatabase());

        pathGrantService.Verify(p => p.RemoveAll(entry.Sid), Times.Once);
        sidProvider.Verify(s => s.GetSidString(It.IsAny<string>()), Times.Never);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RevertTraverseAccess_ReturnsRemoveAllWarnings()
    {
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostRemoveAllSave,
            @"C:\ContainerRoot",
            null,
            new InvalidOperationException("save failed"));
        var expected = new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: false,
            Warnings: [warning]);
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService.Setup(p => p.RemoveAll("S-1-15-2-99-test")).Returns(expected);
        var sidProvider = new Mock<IAppContainerSidProvider>();
        sidProvider.Setup(s => s.GetSidString("ram_test")).Returns("S-1-15-2-99-test");
        var service = CreateService(pathGrantService: pathGrantService.Object, sidProvider: sidProvider.Object);

        var result = service.RevertTraverseAccess(new AppContainerEntry { Name = "ram_test" }, new AppDatabase());

        Assert.Equal(expected, result);
    }

    private AppContainerService CreateService(
        IPathGrantService? pathGrantService = null,
        IAppContainerProfileSetup? profileSetup = null,
        IAppContainerDataFolderService? dataFolderService = null,
        IAppContainerComAccessService? comAccessService = null,
        IExplorerTokenProvider? explorerTokenProvider = null,
        IAppContainerSidProvider? sidProvider = null,
        IAppContainerUserRegistryRoot? userRegistryRoot = null,
        IAppContainerPathProvider? pathProvider = null,
        int deleteProfileHResult = 0)
    {
        var log = new Mock<ILoggingService>();
        var explorerProvider = explorerTokenProvider ?? CreateExplorerTokenProvider().Object;
        return new TestAppContainerService(
            log.Object,
            pathGrantService ?? new Mock<IPathGrantService>().Object,
            profileSetup ?? new Mock<IAppContainerProfileSetup>().Object,
            dataFolderService ?? new Mock<IAppContainerDataFolderService>().Object,
            comAccessService ?? new Mock<IAppContainerComAccessService>().Object,
            explorerProvider,
            sidProvider ?? new AppContainerSidProvider(),
            userRegistryRoot ?? AppContainerProviderTestDoubles.CreateUserRegistryRoot(_registry.HkuRoot),
            pathProvider ?? AppContainerProviderTestDoubles.CreatePathProvider(_containersRootPath),
            deleteProfileHResult);
    }

    private static Mock<IExplorerTokenProvider> CreateExplorerTokenProvider()
    {
        var provider = new Mock<IExplorerTokenProvider>();
        provider.Setup(p => p.TryGetExplorerToken()).Returns(() => OpenCurrentProcessToken());
        provider.Setup(p => p.GetExplorerToken()).Returns(() => OpenCurrentProcessToken());
        return provider;
    }

    private static IntPtr OpenCurrentProcessToken()
    {
        if (!ProcessNative.OpenProcessToken(
                Process.GetCurrentProcess().Handle,
                ProcessLaunchNative.TOKEN_QUERY,
                out var token))
        {
            throw new InvalidOperationException("Unable to open the current process token for test setup.");
        }

        return token;
    }

    private void CreateContainerState(string hiveName, string containerSid)
    {
        _registry.HkuRoot.CreateSubKey(
            $@"{hiveName}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\{containerSid}")
            ?.Dispose();
        _registry.HkuRoot.CreateSubKey(
            $@"{hiveName}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Storage\{containerSid}\User Shell Folders")
            ?.Dispose();
    }

    private sealed class TestAppContainerService(
        ILoggingService log,
        IPathGrantService pathGrantService,
        IAppContainerProfileSetup profileSetup,
        IAppContainerDataFolderService dataFolderService,
        IAppContainerComAccessService comAccessService,
        IExplorerTokenProvider explorerTokenProvider,
        IAppContainerSidProvider sidProvider,
        IAppContainerUserRegistryRoot userRegistryRoot,
        IAppContainerPathProvider pathProvider,
        int deleteProfileHResult)
        : AppContainerService(
            log,
            pathGrantService,
            profileSetup,
            dataFolderService,
            comAccessService,
            explorerTokenProvider,
            sidProvider,
            userRegistryRoot,
            pathProvider)
    {
        protected override int DeleteAppContainerProfile(string name)
            => deleteProfileHResult;
    }
}

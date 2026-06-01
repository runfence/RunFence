using Moq;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalAccountStateServiceTests : IDisposable
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalAccountState");
    private readonly RegistryTestHelper _registry = new("WindowsTerminalAccountState_HKU", "WindowsTerminalAccountState_HKLM");

    [Fact]
    public void IsInstalledForAccount_WhenNativeWtExists_ReturnsTrue_AndLaunchUsesNativeWt()
    {
        var profileRoot = CreateProfileRoot();
        var nativeWtPath = Path.Combine(profileRoot, "AppData", "Local", "Microsoft", "WindowsApps", "wt.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeWtPath)!);
        File.WriteAllText(nativeWtPath, string.Empty);

        var service = CreateService(profileRoot);

        Assert.True(service.IsInstalledForAccount(TestSid));
        Assert.Equal(nativeWtPath, service.ResolveLaunchTarget(TestSid));
    }

    [Fact]
    public void SharedExecutablePresent_PathAbsentAndNativeMissing_IsNotInstalled_ButLaunchUsesSharedExecutable()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        File.WriteAllText(paths.GetSharedExecutablePath(PrivilegeLevel.Isolated), string.Empty);

        var service = CreateService(profileRoot: null, paths: paths);

        Assert.False(service.IsInstalledForAccount(TestSid));
        Assert.Equal(paths.SharedExecutablePath, service.ResolveLaunchTarget(TestSid));
        Assert.Equal(
            paths.GetSharedExecutablePath(PrivilegeLevel.Isolated),
            service.ResolveLaunchTarget(new AccountLaunchIdentity(TestSid)));
    }

    [Fact]
    public void SharedPathRegistered_SharedExecutableAbsentAndNativeMissing_IsInstalled_ButLaunchFallsBackToCmd()
    {
        var paths = CreatePaths();
        using var environmentKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Environment");
        environmentKey!.SetValue("Path", $@"C:\Tools;{paths.SharedHelperPathDirectory}\;C:\Other");

        var service = CreateService(profileRoot: null, paths: paths);

        Assert.True(service.IsInstalledForAccount(TestSid));
        Assert.Equal("cmd.exe", service.ResolveLaunchTarget(TestSid));
    }

    [Fact]
    public void SharedExecutablePresent_PrefersSharedExecutableOverNativeWt()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        File.WriteAllText(paths.GetSharedExecutablePath(PrivilegeLevel.Isolated), string.Empty);

        var profileRoot = CreateProfileRoot();
        var nativeWtPath = Path.Combine(profileRoot, "AppData", "Local", "Microsoft", "WindowsApps", "wt.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeWtPath)!);
        File.WriteAllText(nativeWtPath, string.Empty);

        var service = CreateService(profileRoot, paths);

        Assert.Equal(paths.SharedExecutablePath, service.ResolveLaunchTarget(TestSid));
    }

    [Fact]
    public void ResolveLaunchTarget_WhenStoredPrivilegeUsesManagedVariant_ReturnsVariant()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        File.WriteAllText(paths.GetSharedExecutablePath(PrivilegeLevel.HighIntegrity), string.Empty);

        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.HighIntegrity;
        var service = CreateService(profileRoot: null, paths: paths, database: database);

        var launchTarget = service.ResolveLaunchTarget(new AccountLaunchIdentity(TestSid));

        Assert.Equal(paths.GetSharedExecutablePath(PrivilegeLevel.HighIntegrity), launchTarget);
    }

    [Fact]
    public void ResolveLaunchTarget_WhenRequestedVariantIsMissing_FallsBackToBasicSharedExecutable()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);

        var service = CreateService(profileRoot: null, paths: paths);

        var launchTarget = service.ResolveLaunchTarget(
            new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity });

        Assert.Equal(paths.SharedExecutablePath, launchTarget);
    }

    [Fact]
    public void ResolveLaunchTarget_WhenStoredPrivilegeIsIsolated_UsesPreparedLaunchCopy()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        File.WriteAllText(paths.GetSharedExecutablePath(PrivilegeLevel.Isolated), string.Empty);
        var isolatedLaunchPath = paths.CreatePrivilegeLaunchExecutablePath(PrivilegeLevel.Isolated);

        var privilegeLaunchExecutableService = new Mock<IWindowsTerminalPrivilegeLaunchExecutableService>();
        privilegeLaunchExecutableService.Setup(service => service.PrepareLaunchExecutablePath(PrivilegeLevel.Isolated)).Returns(isolatedLaunchPath);

        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.Isolated;
        var service = CreateService(
            profileRoot: null,
            paths: paths,
            database: database,
            privilegeLaunchExecutableService: privilegeLaunchExecutableService.Object);

        var launchTarget = service.ResolveLaunchTarget(new AccountLaunchIdentity(TestSid));

        Assert.Equal(isolatedLaunchPath, launchTarget);
        privilegeLaunchExecutableService.Verify(service => service.PrepareLaunchExecutablePath(PrivilegeLevel.Isolated), Times.Once);
    }

    [Fact]
    public void ResolveLaunchTarget_WhenStoredPrivilegeIsLowIntegrity_UsesPreparedLaunchCopy()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        File.WriteAllText(paths.GetSharedExecutablePath(PrivilegeLevel.LowIntegrity), string.Empty);
        var lowIntegrityLaunchPath = paths.CreatePrivilegeLaunchExecutablePath(PrivilegeLevel.LowIntegrity);

        var privilegeLaunchExecutableService = new Mock<IWindowsTerminalPrivilegeLaunchExecutableService>();
        privilegeLaunchExecutableService.Setup(service => service.PrepareLaunchExecutablePath(PrivilegeLevel.LowIntegrity)).Returns(lowIntegrityLaunchPath);

        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.LowIntegrity;
        var service = CreateService(
            profileRoot: null,
            paths: paths,
            database: database,
            privilegeLaunchExecutableService: privilegeLaunchExecutableService.Object);

        var launchTarget = service.ResolveLaunchTarget(new AccountLaunchIdentity(TestSid));

        Assert.Equal(lowIntegrityLaunchPath, launchTarget);
        privilegeLaunchExecutableService.Verify(service => service.PrepareLaunchExecutablePath(PrivilegeLevel.LowIntegrity), Times.Once);
    }

    public void Dispose()
    {
        _registry.Dispose();
        _tempDirectory.Dispose();
    }

    private WindowsTerminalAccountStateService CreateService(
        string? profileRoot,
        WindowsTerminalDeploymentPaths? paths = null,
        AppDatabase? database = null,
        IWindowsTerminalPrivilegeLaunchExecutableService? privilegeLaunchExecutableService = null)
    {
        var resolvedPaths = paths ?? CreatePaths();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns((string?)profileRoot);
        var toolResolver = new AccountToolResolver(profilePathResolver.Object);
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database ?? new AppDatabase());
        var resolvedPrivilegeLaunchExecutableService = privilegeLaunchExecutableService ?? CreateDefaultPrivilegeLaunchExecutableService(resolvedPaths);
        return new WindowsTerminalAccountStateService(
            toolResolver,
            databaseProvider.Object,
            _registry.HiveManager.Object,
            new TestHkuRootProvider(_registry.HkuRoot),
            resolvedPaths,
            resolvedPrivilegeLaunchExecutableService);
    }

    private static IWindowsTerminalPrivilegeLaunchExecutableService CreateDefaultPrivilegeLaunchExecutableService(WindowsTerminalDeploymentPaths paths)
    {
        var service = new Mock<IWindowsTerminalPrivilegeLaunchExecutableService>();
        service.Setup(launchService => launchService.PrepareLaunchExecutablePath(PrivilegeLevel.Isolated))
            .Returns(paths.GetSharedExecutablePath(PrivilegeLevel.Isolated));
        service.Setup(launchService => launchService.PrepareLaunchExecutablePath(PrivilegeLevel.LowIntegrity))
            .Returns(paths.SharedExecutablePath);
        return service.Object;
    }

    private WindowsTerminalDeploymentPaths CreatePaths()
        => new(new TestProgramDataKnownPathResolver(_tempDirectory.Path));

    private string CreateProfileRoot()
    {
        var profileRoot = Path.Combine(_tempDirectory.Path, "Profile");
        Directory.CreateDirectory(profileRoot);
        return profileRoot;
    }

    private sealed class TestHkuRootProvider(InMemoryRegistryKey usersRoot) : IHkuRootProvider
    {
        public IRegistryKey OpenUsersRoot() => usersRoot;
    }
}

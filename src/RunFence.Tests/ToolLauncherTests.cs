using Moq;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class ToolLauncherTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public async Task OpenCmd_WindowsTerminal_KeepsStoredHighIntegrityDefault()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.HighIntegrity;
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var windowsAppsPath = Path.Combine(profileRoot, "AppData", "Local", "Microsoft", "WindowsApps");
        Directory.CreateDirectory(windowsAppsPath);
        File.WriteAllText(Path.Combine(windowsAppsPath, "wt.exe"), string.Empty);

        try
        {
            var databaseProvider = new Mock<IDatabaseProvider>();
            databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database);

            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));

            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var toolResolver = new AccountToolResolver(profilePathResolver.Object);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService.Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(Path.Combine(windowsAppsPath, "wt.exe"));

            var launcher = new ToolLauncher(
                launchFacade.Object,
                toolResolver,
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot))),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                CreateLaunchRefreshService().Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase)),
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == null),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenCmd_SharedWindowsTerminal_UsesManagedVariantForStoredDefaultPrivilege()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.Isolated;
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database);
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, string.Empty);
        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated), string.Empty);
        var isolatedLaunchPath = deploymentPaths.CreatePrivilegeLaunchExecutablePath(PrivilegeLevel.Isolated);
        File.WriteAllText(isolatedLaunchPath, string.Empty);
        File.WriteAllText(isolatedLaunchPath, string.Empty);

        try
        {
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(isolatedLaunchPath);

            var launcher = new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                CreateLaunchRefreshService().Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath == isolatedLaunchPath),
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == null),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public async Task OpenCmd_RestrictedSharedWindowsTerminal_LaunchesOnlyRequestedTarget(PrivilegeLevel privilegeLevel)
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = privilegeLevel;
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database);
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, string.Empty);
        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(privilegeLevel), string.Empty);
        var launchPath = deploymentPaths.CreatePrivilegeLaunchExecutablePath(privilegeLevel);
        File.WriteAllText(launchPath, string.Empty);

        try
        {
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(launchPath);

            var launcher = new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                CreateLaunchRefreshService().Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath == launchPath),
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == null),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.IsAny<ProcessLaunchTarget>(),
                    It.Is<AccountLaunchIdentity>(identity => identity.PrivilegeLevel == PrivilegeLevel.Basic),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Never);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenCmd_WhenSharedDeploymentIsMissing_EnsuresDeploymentBeforeLaunchingManagedTerminal()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.Isolated;
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database);
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));

        try
        {
            var calls = new List<string>();
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Callback(() => calls.Add("launch"))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var toolResolver = new AccountToolResolver(profilePathResolver.Object);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(() => File.Exists(deploymentPaths.SharedExecutablePath)
                    ? deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated)
                    : "cmd.exe");
            var launchRefreshService = CreateLaunchRefreshService(() =>
            {
                calls.Add("ensure");
                Directory.CreateDirectory(deploymentPaths.SharedRootPath);
                File.WriteAllText(deploymentPaths.SharedExecutablePath, string.Empty);
                File.WriteAllText(deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated), string.Empty);
            });
            var launcher = new ToolLauncher(
                launchFacade.Object,
                toolResolver,
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                launchRefreshService.Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            Assert.Equal(["ensure", "launch"], calls);
            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath == deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated)),
                    It.IsAny<AccountLaunchIdentity>(),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
            launchRefreshService.Verify(
                service => service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid)),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenCmd_WhenSharedDeploymentIsMissingAndSystemWtExists_FallsBackToNativeWt()
    {
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));
        var nativeWtPath = Path.Combine(profileRoot, "AppData", "Local", "Microsoft", "WindowsApps", "wt.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeWtPath)!);
        File.WriteAllText(nativeWtPath, string.Empty);

        try
        {
            var databaseProvider = new Mock<IDatabaseProvider>();
            databaseProvider.Setup(provider => provider.GetDatabase()).Returns(new AppDatabase());
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(() => File.Exists(deploymentPaths.SharedExecutablePath) ? deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated) : nativeWtPath);

            var launcher = new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                CreateLaunchRefreshService().Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath == nativeWtPath),
                    It.IsAny<AccountLaunchIdentity>(),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenCmd_WhenSharedDeploymentIsMissingAndSystemWtDoesNotExist_FallsBackToCmd()
    {
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot));

        try
        {
            var databaseProvider = new Mock<IDatabaseProvider>();
            databaseProvider.Setup(provider => provider.GetDatabase()).Returns(new AppDatabase());
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns(() => File.Exists(deploymentPaths.SharedExecutablePath) ? deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated) : "cmd.exe");

            var launcher = new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                CreateLaunchRefreshService().Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            await launcher.OpenCmdAsync(new AccountLaunchIdentity(TestSid));

            launchFacade.Verify(
                facade => facade.LaunchFile(
                    It.Is<ProcessLaunchTarget>(target => target.ExePath == "cmd.exe"),
                    It.IsAny<AccountLaunchIdentity>(),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenTerminalForAccount_WithRefreshIdentity_RequestsRefreshAndReturnsStatus()
    {
        var profileRoot = Path.Combine(Path.GetTempPath(), $"RunFence.ToolLauncher.{Guid.NewGuid():N}");

        try
        {
            var databaseProvider = new Mock<IDatabaseProvider>();
            databaseProvider.Setup(provider => provider.GetDatabase()).Returns(new AppDatabase());
            var launchFacade = new Mock<ILaunchFacade>();
            launchFacade
                .Setup(facade => facade.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
                .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
            var profilePathResolver = new Mock<IProfilePathResolver>();
            profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(profileRoot);
            var windowsTerminalAccountStateService = new Mock<IWindowsTerminalAccountStateService>();
            windowsTerminalAccountStateService
                .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
                .Returns("cmd.exe");
            var launchRefreshService = CreateLaunchRefreshService();
            var launcher = new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                windowsTerminalAccountStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(profileRoot))),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                launchRefreshService.Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>());

            var status = await launcher.OpenTerminalForAccountAsync(
                new AccountLaunchIdentity(TestSid),
                requestTerminalRefresh: true);

            Assert.Equal("The terminal", status.StartedItem);
            Assert.Equal("cmd.exe", status.SummaryName);
            Assert.True(status.RefreshRequested);
            launchRefreshService.Verify(
                service => service.TryStartOnlineRefreshAfterTerminalLaunch(
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid)),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }

    private static Mock<IWindowsTerminalLaunchRefreshService> CreateLaunchRefreshService(Action? ensureAction = null)
    {
        var launchRefreshService = new Mock<IWindowsTerminalLaunchRefreshService>();
        launchRefreshService
            .Setup(service => service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(It.IsAny<LaunchIdentity>()))
            .Callback(() => ensureAction?.Invoke())
            .Returns(Task.CompletedTask);
        return launchRefreshService;
    }

}

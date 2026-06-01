using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class AccountContextMenuOrchestratorTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void CmdContextMenuClick_ForAccountRow_LaunchesImmediatelyAndStartsOnlineRefreshAfterLaunch()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var calls = new List<string>();
            using var context = CreateContext(calls);
            SelectRow(context.Grid, new AccountRow(null, "TerminalUser", TestSid, hasStoredPassword: true));

            await context.Orchestrator.OpenCmdWithTerminalLaunchRefreshAsync();

            Assert.Equal(["launch", "online"], calls);
            context.RefreshService.Verify(
                service => service.TryStartOnlineRefreshAfterTerminalLaunch(
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == null)),
                Times.Once);
            context.LaunchFacade.Verify(
                facade => facade.LaunchFile(
                    It.IsAny<ProcessLaunchTarget>(),
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        });
    }

    [Fact]
    public void CmdContextMenuClick_ForContainerRow_LaunchesCmdWithoutTerminalRefresh()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var calls = new List<string>();
            using var context = CreateContext(calls);
            var container = new AppContainerEntry { Name = "TestContainer", Sid = "S-1-15-2-1" };
            SelectRow(context.Grid, new ContainerRow(container, container.Sid));

            await context.Orchestrator.OpenCmdWithTerminalLaunchRefreshAsync();

            Assert.Equal(["launch"], calls);
            context.RefreshService.Verify(
                service => service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(It.IsAny<LaunchIdentity>()),
                Times.Never);
            context.LaunchFacade.Verify(
                facade => facade.LaunchFile(
                    It.IsAny<ProcessLaunchTarget>(),
                    It.Is<AppContainerLaunchIdentity>(identity => identity.Entry == container),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        });
    }

    [Fact]
    public void OpenCmdWithExplicitPrivilege_ForAccountRow_UsesRequestedPrivilegeLevel()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var calls = new List<string>();
            using var context = CreateContext(calls);
            SelectRow(context.Grid, new AccountRow(null, "TerminalUser", TestSid, hasStoredPassword: true));

            await context.Orchestrator.OpenCmdWithTerminalLaunchRefreshAsync(PrivilegeLevel.LowIntegrity);

            Assert.Equal(["launch", "online"], calls);
            context.RefreshService.Verify(
                service => service.TryStartOnlineRefreshAfterTerminalLaunch(
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == PrivilegeLevel.LowIntegrity)),
                Times.Once);
            context.LaunchFacade.Verify(
                facade => facade.LaunchFile(
                    It.IsAny<ProcessLaunchTarget>(),
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == PrivilegeLevel.LowIntegrity),
                    It.IsAny<Func<string, string, bool>?>()),
                Times.Once);
        });
    }

    [Fact]
    public void OpenFolderBrowserWithExplicitPrivilege_ForAccountRow_UsesRequestedPrivilegeLevel()
    {
        StaTestHelper.RunAsyncOnSta(() =>
        {
            using var context = CreateContext([]);
            SelectRow(context.Grid, new AccountRow(null, "TerminalUser", TestSid, hasStoredPassword: true));

            context.Orchestrator.OpenFolderBrowser(PrivilegeLevel.HighIntegrity);

            context.LaunchFacade.Verify(
                facade => facade.LaunchFolderBrowser(
                    It.Is<AccountLaunchIdentity>(identity => identity.Sid == TestSid && identity.PrivilegeLevel == PrivilegeLevel.HighIntegrity),
                    It.IsAny<string?>(),
                    It.IsAny<Func<string, string, bool>?>(),
                    It.IsAny<bool>()),
                Times.Once);
            return Task.CompletedTask;
        });
    }

    private static TestContext CreateContext(List<string> calls)
    {
        var grid = new DataGridView
        {
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        grid.Columns.Add("Name", "Name");

        var contextMenu = new ContextMenuStrip();
        var ownerControl = new Panel();
        var panelContext = new Mock<IAccountsPanelOperationContext>();
        panelContext.SetupGet(context => context.OwnerControl).Returns(ownerControl);
        panelContext.SetupGet(context => context.OperationGuard).Returns(new OperationGuard());
        panelContext.Setup(context => context.BeginProcessRefreshGeneration()).Returns(1);
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(facade => facade.LaunchFile(
                It.IsAny<ProcessLaunchTarget>(),
                It.IsAny<LaunchIdentity>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Callback(() => calls.Add("launch"))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        launchFacade
            .Setup(facade => facade.LaunchFolderBrowser(
                It.IsAny<LaunchIdentity>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<bool>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));

        var profilePathResolver = new Mock<IProfilePathResolver>();
        profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(TestSid)).Returns(Path.GetTempPath());

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(new AppDatabase());

        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()));
        var terminalStateService = new Mock<IWindowsTerminalAccountStateService>();
        terminalStateService
            .Setup(service => service.ResolveLaunchTarget(TestSid))
            .Returns(deploymentPaths.SharedExecutablePath);
        terminalStateService
            .Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>()))
            .Returns(deploymentPaths.SharedExecutablePath);

        var refreshService = new Mock<IWindowsTerminalLaunchRefreshService>();
        refreshService
            .Setup(service => service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(It.IsAny<LaunchIdentity>()))
            .Returns(Task.CompletedTask);
        refreshService
            .Setup(service => service.TryStartOnlineRefreshAfterTerminalLaunch(It.IsAny<AccountLaunchIdentity>()))
            .Callback(() => calls.Add("online"));

        var localGroupQueryService = new Mock<ILocalGroupQueryService>();
        localGroupQueryService.Setup(service => service.GetGroupsForUser(TestSid)).Returns([]);

        var accountMenuStateConfigurator = new AccountMenuStateConfigurator(
            Mock.Of<IWindowsAccountQueryService>(),
            Mock.Of<ISessionProvider>(),
            terminalStateService.Object,
            Mock.Of<IPackageInstallService>());

        var orchestrator = new AccountContextMenuOrchestrator(
            new AccountContextMenuHandler(
                accountMenuStateConfigurator,
                Mock.Of<ISessionProvider>(),
                Mock.Of<IProcessTerminationService>(),
                Mock.Of<IAccountMessageBoxService>()),
            new ContainerContextMenuHandler(
                new AccountContainerOrchestrator(null!, null!, null!, null!, null!, null!, null!),
                new AppContainerProfileActions(null!, null!, null!)),
            new AccountFirewallMenuHandler(null!, null!, null!, null!),
            new AccountProcessMenuHandler(
                Mock.Of<IShellHelper>(),
                new ProcessCommandLineFormatter(),
                Mock.Of<IProcessTerminationService>()),
            new ToolLauncher(
                launchFacade.Object,
                new AccountToolResolver(profilePathResolver.Object),
                terminalStateService.Object,
                new TerminalLaunchIdentitySelector(databaseProvider.Object, deploymentPaths),
                Mock.Of<IPackageInstallService>(),
                Mock.Of<IWindowsTerminalDeploymentProgressRunner>(),
                refreshService.Object,
                Mock.Of<ILaunchFeedbackPresenter>(),
                Mock.Of<ILoggingService>()),
            new AccountTrayToggleService(null!, null!, null!, null!),
            Mock.Of<ISidNameCacheService>(),
            new FullModeAccountLaunchIdentityFactory(localGroupQueryService.Object));

        orchestrator.Initialize(
            grid,
            contextMenu,
            panelContext.Object,
            new ToolStripMenuItem("Create container"));

        return new TestContext(orchestrator, grid, contextMenu, ownerControl, launchFacade, refreshService);
    }

    private static void SelectRow(DataGridView grid, object tag)
    {
        var rowIndex = grid.Rows.Add(tag.GetType().Name);
        grid.ClearSelection();
        grid.Rows[rowIndex].Tag = tag;
        grid.Rows[rowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[rowIndex].Cells[0];
    }

    private sealed record TestContext(
        AccountContextMenuOrchestrator Orchestrator,
        DataGridView Grid,
        ContextMenuStrip ContextMenu,
        Control OwnerControl,
        Mock<ILaunchFacade> LaunchFacade,
        Mock<IWindowsTerminalLaunchRefreshService> RefreshService) : IDisposable
    {
        public void Dispose()
        {
            ContextMenu.Dispose();
            Grid.Dispose();
            OwnerControl.Dispose();
        }
    }
}

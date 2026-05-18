using System.ComponentModel;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.RunAs;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class IpcMessageHandlerTests
{
    private readonly Mock<IAppStateProvider> _appState;
    private readonly Mock<IAppLockControl> _appLock;
    private readonly Mock<IElevatedUnlockRequestHandler> _elevatedUnlockRequestHandler;
    private readonly Mock<IOperationUnlockRequestHandler> _operationUnlockRequestHandler;
    private readonly Mock<IShowWindowRequestHandler> _showWindowRequestHandler;
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker;
    private readonly Mock<IAppEntryLauncher> _orchestrator;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IIdleMonitorService> _idleMonitor;
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<ITrayBalloonService> _trayBalloon = new();
    private readonly IpcMessageHandler _handler;
    private readonly AppDatabase _database;

    public IpcMessageHandlerTests()
    {
        _database = new AppDatabase();
        _appState = new Mock<IAppStateProvider>();
        _appLock = new Mock<IAppLockControl>();
        _elevatedUnlockRequestHandler = new Mock<IElevatedUnlockRequestHandler>();
        _operationUnlockRequestHandler = new Mock<IOperationUnlockRequestHandler>();
        _showWindowRequestHandler = new Mock<IShowWindowRequestHandler>();
        _uiThreadInvoker = new Mock<IUiThreadInvoker>();

        _appState.Setup(c => c.Database).Returns(_database);
        _appState.Setup(c => c.IsShuttingDown).Returns(false);
        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());
        _uiThreadInvoker.Setup(c => c.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());
        _elevatedUnlockRequestHandler.Setup(c => c.HandleElevatedUnlockRequestAsync()).ReturnsAsync(true);
        _operationUnlockRequestHandler.Setup(c => c.HandleOperationUnlockRequestAsync()).ReturnsAsync(true);
        _showWindowRequestHandler.Setup(c => c.RequestShowWindow());

        _orchestrator = new Mock<IAppEntryLauncher>();
        _orchestrator
            .Setup(o => o.Launch(
                It.IsAny<AppEntry>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Returns(() => new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        _log = new Mock<ILoggingService>();
        _idleMonitor = new Mock<IIdleMonitorService>();

        _handler = CreateHandler();
    }

    private IpcMessageHandler CreateHandler(
        IIdleMonitorService? idleMonitor = null,
        IConfigManagementContext? configContext = null,
        IRunAsFlowHandler? runAsFlowHandler = null,
        IpcAssociationHandler? associationHandler = null)
    {
        var resolvedIdleMonitor = idleMonitor ?? _idleMonitor.Object;
        var sidResolver = new Mock<ISidResolver>();
        var authorizer = new IpcCallerAuthorizer(_log.Object, sidResolver.Object);
        var ipcUiInvoker = new IpcUiInvoker(_uiThreadInvoker.Object, _appState.Object);
        var configHandler = new IpcConfigHandler(
            _appState.Object, _appLock.Object, ipcUiInvoker,
            resolvedIdleMonitor, _log.Object, configContext);
        var lifecycleHandler = new IpcLifecycleHandler(
            _appState.Object, _appLock.Object, ipcUiInvoker,
            _elevatedUnlockRequestHandler.Object, _operationUnlockRequestHandler.Object,
            _showWindowRequestHandler.Object, _log.Object);
        var launchFeedbackPresenter = new LaunchFeedbackPresenter(
            _log.Object,
            new Mock<IAccountMessageBoxService>().Object,
            new MockTrayWarningSink(_trayBalloon.Object),
            new SystemClock());
        var launchHandler = new IpcLaunchHandler(
            _appState.Object, _appLock.Object, ipcUiInvoker,
            _orchestrator.Object, authorizer, _sidNameCache.Object,
            resolvedIdleMonitor, launchFeedbackPresenter, runAsFlowHandler);
        var openFolderHandler = new IpcOpenFolderHandler(
            _appLock.Object, ipcUiInvoker,
            null, _log.Object, new ShellFolderOpener());
        return new IpcMessageHandler(
            _log.Object, configHandler, lifecycleHandler, launchHandler, openFolderHandler, associationHandler);
    }

    private IpcMessageHandler CreateHandlerWithConfig(Mock<IConfigManagementContext> configContext)
        => CreateHandler(configContext: configContext.Object);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Launch_NullOrEmptyAppId_ReturnsError(string? appId)
    {
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = appId };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("AppId is required.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_UnknownAppId_ReturnsNotFound()
    {
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "XXXXX" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Application not found.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_UnauthorizedCaller_ReturnsAccessDenied()
    {
        var app = new AppEntry
        {
            Id = "app01",
            Name = "TestApp",
            AllowedIpcCallers = ["S-1-5-21-0-0-0-1001"]
        };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Denied", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Access denied.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_AuthorizedCaller_LaunchesAndReturnsSuccess()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage
        {
            Command = IpcCommands.Launch,
            AppId = "app01",
            Arguments = "--flag"
        };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.True(result.Success);
        _orchestrator.Verify(o => o.Launch(app, message.Arguments, message.WorkingDirectory, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    [Fact]
    public void Launch_ShuttingDown_ReturnsError()
    {
        _appState.Setup(c => c.IsShuttingDown).Returns(true);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Application is shutting down.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_InvokeThrowsObjectDisposed_ReturnsShuttingDown()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>()))
            .Throws(new ObjectDisposedException("MainForm"));

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Application is shutting down.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_InvokeThrowsInvalidOperation_ReturnsShuttingDown()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>()))
            .Throws(new InvalidOperationException("Handle not created"));

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Application is shutting down.", result.ErrorMessage);
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var message = new IpcMessage { Command = "FooBar" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Unknown command: FooBar", result.ErrorMessage);
    }

    [Fact]
    public void Launch_NullArguments_PassesNullToOrchestrator()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01", Arguments = null };

        _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        _orchestrator.Verify(o => o.Launch(app, null, null, It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }

    // --- Shutdown command tests ---

    [Fact]
    public void Shutdown_NonAdmin_ReturnsAccessDenied()
    {
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("Admin required", result.ErrorMessage);
    }

    [Fact]
    public void Shutdown_Admin_ReturnsSuccessAndSchedulesExit()
    {
        _appState.Setup(c => c.IsOperationInProgress).Returns(false);
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.True(result.Success);
        _uiThreadInvoker.Verify(c => c.BeginInvoke(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void Shutdown_Admin_OperationInProgress_ReturnsError()
    {
        _appState.Setup(c => c.IsOperationInProgress).Returns(true);
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("Operation in progress", result.ErrorMessage);
    }

    // --- Unlock command tests ---

    [Fact]
    public void Unlock_NonAdmin_ReturnsAccessDenied()
    {
        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("Admin required", result.ErrorMessage);
    }

    [Fact]
    public void Unlock_DifferentAdminCaller_UsesNormalShowFlow_AndReturnsSuccess()
    {
        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.True(result.Success);
        _showWindowRequestHandler.Verify(c => c.RequestShowWindow(), Times.Once);
        _elevatedUnlockRequestHandler.Verify(c => c.HandleElevatedUnlockRequestAsync(), Times.Never);
    }

    [Fact]
    public void Unlock_Admin_InvokeThrows_ObjectDisposed_ReturnsShuttingDown()
    {
        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>()))
            .Throws(new ObjectDisposedException("MainForm"));

        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("shutting down", result.ErrorMessage);
    }

    [Fact]
    public void Unlock_Admin_StillLockedAfterUnlock_ReturnsCancelled()
    {
        _elevatedUnlockRequestHandler.Setup(c => c.HandleElevatedUnlockRequestAsync()).ReturnsAsync(false);

        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(
            message,
            new IpcCallerContext(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true, true));

        Assert.False(result.Success);
        Assert.Equal("Unlock cancelled.", result.ErrorMessage);
        _elevatedUnlockRequestHandler.Verify(c => c.HandleElevatedUnlockRequestAsync(), Times.Once);
    }

    [Fact]
    public async Task Unlock_Admin_WaitsForAsyncUnlockCompletion()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unlockCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _elevatedUnlockRequestHandler
            .Setup(c => c.HandleElevatedUnlockRequestAsync())
            .Returns(() =>
            {
                started.TrySetResult();
                return unlockCompletion.Task;
            });

        IpcResponse? result = null;
        var handlerTask = Task.Run(() =>
        {
            result = _handler.HandleIpcMessage(
                new IpcMessage { Command = IpcCommands.Unlock },
                new IpcCallerContext(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true, true));
            completed.TrySetResult();
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(completed.Task.IsCompleted, "Unlock should block until completion task resolves.");
        unlockCompletion.SetResult(true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await handlerTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public void Unlock_CurrentAdminCaller_UsesDirectUnlockPath()
    {
        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(
            message,
            new IpcCallerContext(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true, true));

        Assert.True(result.Success);
        _elevatedUnlockRequestHandler.Verify(c => c.HandleElevatedUnlockRequestAsync(), Times.Once);
        _showWindowRequestHandler.Verify(c => c.RequestShowWindow(), Times.Never);
    }

    [Fact]
    public void UnlockOperation_CurrentAdminCaller_CompletesPendingOperationUnlock()
    {
        var message = new IpcMessage { Command = IpcCommands.UnlockOperation };

        var result = _handler.HandleIpcMessage(
            message,
            new IpcCallerContext(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true, true));

        Assert.True(result.Success);
        _operationUnlockRequestHandler.Verify(c => c.HandleOperationUnlockRequestAsync(), Times.Once);
        _elevatedUnlockRequestHandler.Verify(c => c.HandleElevatedUnlockRequestAsync(), Times.Never);
        _showWindowRequestHandler.Verify(c => c.RequestShowWindow(), Times.Never);
    }

    [Fact]
    public void UnlockOperation_DifferentAdminCaller_RequestsOperationUnlockFlow()
    {
        var message = new IpcMessage { Command = IpcCommands.UnlockOperation };

        var result = _handler.HandleIpcMessage(
            message,
            new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.True(result.Success);
        _operationUnlockRequestHandler.Verify(c => c.HandleOperationUnlockRequestAsync(), Times.Never);
        _operationUnlockRequestHandler.Verify(c => c.RequestOperationUnlock(), Times.Once);
        _showWindowRequestHandler.Verify(c => c.RequestShowWindow(), Times.Never);
    }

    [Fact]
    public void UnlockOperation_NoPendingOperation_ReturnsError()
    {
        _operationUnlockRequestHandler.Setup(c => c.HandleOperationUnlockRequestAsync()).ReturnsAsync(false);
        var message = new IpcMessage { Command = IpcCommands.UnlockOperation };

        var result = _handler.HandleIpcMessage(
            message,
            new IpcCallerContext(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true, true));

        Assert.False(result.Success);
        Assert.Equal("No pending operation unlock.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_Win32LogonFailure_ReturnsSuccessAndLogsError()
    {
        // Auth succeeds → Success = true. Launch errors are fire-and-forget: logged but not returned to caller.
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        _orchestrator
            .Setup(o => o.Launch(app, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(new Win32Exception(ProcessLaunchNative.Win32ErrorLogonFailure));

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.True(result.Success);
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("credentials are incorrect")), It.IsAny<Exception>()), Times.Once);
    }

    // --- Idle monitor integration ---

    [Fact]
    public void Launch_Success_ResetsIdleMonitor()
    {
        var idleMonitor = new Mock<IIdleMonitorService>();
        var handler = CreateHandler(idleMonitor: idleMonitor.Object);

        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };
        handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Once);
    }

    [Fact]
    public void Launch_NoIdleMonitor_DoesNotThrow()
    {
        // Default handler has no idle monitor (null)
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };
        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.True(result.Success);
    }

    // --- T-4: IsUnlockPolling=true on Launch returns Busy ---

    [Fact]
    public void Launch_IsUnlockPolling_ReturnsBusy()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);
        _appLock.Setup(c => c.IsUnlockPolling).Returns(true);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Busy.", result.ErrorMessage);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>()), Times.Never);
    }

    // --- T-5: IsModalOpen/IsOperationInProgress guards on config operations ---

    [Theory]
    [InlineData(IpcCommands.LoadApps, true, false)]
    [InlineData(IpcCommands.LoadApps, false, true)]
    [InlineData(IpcCommands.UnloadApps, true, false)]
    [InlineData(IpcCommands.UnloadApps, false, true)]
    public void ConfigOp_BusyGuard_ReturnsOperationInProgress(
        string command, bool isModalOpen, bool isOperationInProgress)
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appState.Setup(c => c.IsModalOpen).Returns(isModalOpen);
        _appState.Setup(c => c.IsOperationInProgress).Returns(isOperationInProgress);
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("Operation in progress", result.ErrorMessage);
        configContext.Verify(c => c.LoadApps(It.IsAny<string>()), Times.Never);
        configContext.Verify(c => c.UnloadApps(It.IsAny<string>()), Times.Never);
    }

    // --- IsRunAsRequest tests ---

    [Theory]
    [InlineData("ab12C", false)]
    [InlineData(@"C:\Apps\test.exe", true)]
    [InlineData("C:/Apps/test.exe", true)]
    [InlineData("", false)]
    public void IsRunAsRequest_DetectsPathSeparators(string appId, bool expected)
    {
        Assert.Equal(expected, RunAsFlowHandler.IsRunAsRequest(appId));
    }

    // --- Path-based AppId when runAsFlowHandler is null (T7a) ---

    [Fact]
    public void Launch_PathBasedAppId_NullRunAsHandler_ReturnsRunAsNotAvailable()
    {
        // _handler was created without runAsFlowHandler → null
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = @"C:\Apps\test.exe" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Run As not available.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_PathBasedAppId_DelegatesToRunAsFlow()
    {
        var runAsFlowHandler = new Mock<IRunAsFlowHandler>();
        var expected = new IpcResponse { Success = true };
        runAsFlowHandler
            .Setup(h => h.HandleRunAs(It.IsAny<IpcMessage>(), It.IsAny<IpcCallerContext>()))
            .Returns(expected);
        var handler = CreateHandler(runAsFlowHandler: runAsFlowHandler.Object);
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = @"C:\Apps\test.exe" };
        var context = new IpcCallerContext(@"DOMAIN\User", null, false, true);

        var result = handler.HandleIpcMessage(message, context);

        Assert.Same(expected, result);
        runAsFlowHandler.Verify(h => h.HandleRunAs(message, context), Times.Once);
        _trayBalloon.Verify(t => t.ShowWarning(It.IsAny<string>()), Times.Never);
    }

    // --- Null config context returns "not available" (T7b) ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_NullConfigContext_ReturnsConfigManagementNotAvailable(string command)
    {
        // _handler has no configContext (null by default in constructor)
        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Equal("Config management not available.", result.ErrorMessage);
    }

    // --- Shutdown during unlock polling ---

    [Fact]
    public void Shutdown_Admin_UnlockPolling_ReturnsError()
    {
        _appState.Setup(c => c.IsOperationInProgress).Returns(false);
        _appLock.Setup(c => c.IsUnlockPolling).Returns(true);
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("Unlock in progress", result.ErrorMessage);
    }

    // --- T-1: LoadApps/UnloadApps non-admin caller ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_NonAdmin_ReturnsAccessDenied(string command)
    {
        var configContext = new Mock<IConfigManagementContext>();
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("Admin required", result.ErrorMessage);
        configContext.Verify(c => c.LoadApps(It.IsAny<string>()), Times.Never);
        configContext.Verify(c => c.UnloadApps(It.IsAny<string>()), Times.Never);
    }

    // --- T-1: IsShuttingDown guard for config operations ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_IsShuttingDown_ReturnsShuttingDown(string command)
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appState.Setup(c => c.IsShuttingDown).Returns(true);
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("shutting down", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        configContext.Verify(c => c.LoadApps(It.IsAny<string>()), Times.Never);
        configContext.Verify(c => c.UnloadApps(It.IsAny<string>()), Times.Never);
    }

    // --- T-1: Null/empty path guard ---

    [Theory]
    [InlineData(IpcCommands.LoadApps, null)]
    [InlineData(IpcCommands.LoadApps, "")]
    [InlineData(IpcCommands.UnloadApps, null)]
    [InlineData(IpcCommands.UnloadApps, "")]
    public void ConfigOp_NullOrEmptyPath_ReturnsPathRequired(string command, string? path)
    {
        var configContext = new Mock<IConfigManagementContext>();
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = command,
            Arguments = path
        };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        configContext.Verify(c => c.LoadApps(It.IsAny<string>()), Times.Never);
        configContext.Verify(c => c.UnloadApps(It.IsAny<string>()), Times.Never);
    }

    // --- T-1: LoadApps failure result ---

    [Fact]
    public void LoadApps_ConfigContextReturnsFailure_ReturnsErrorMessage()
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appLock.Setup(c => c.IsLocked).Returns(false);
        configContext.Setup(c => c.LoadApps(@"C:\test.ramc"))
            .Returns(new LoadAppsResult(false, "Decryption failed."));
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = IpcCommands.LoadApps,
            Arguments = @"C:\test.ramc"
        };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Equal("Decryption failed.", result.ErrorMessage);
    }

    // --- LoadApps/UnloadApps with config context ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_AppLocked_ReturnsLockedError(string command)
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appLock.Setup(c => c.IsLocked).Returns(true);
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.False(result.Success);
        Assert.Equal("App is locked. Unlock first.", result.ErrorMessage);
    }

    [Fact]
    public void LoadApps_Success_DelegatesToConfigContext()
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appLock.Setup(c => c.IsLocked).Returns(false);
        configContext.Setup(c => c.LoadApps(@"C:\test.ramc"))
            .Returns(new LoadAppsResult(true, null));
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = IpcCommands.LoadApps,
            Arguments = @"C:\test.ramc"
        };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.True(result.Success);
        configContext.Verify(c => c.LoadApps(@"C:\test.ramc"), Times.Once);
    }

    [Fact]
    public void UnloadApps_Success_DelegatesToConfigContext()
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appLock.Setup(c => c.IsLocked).Returns(false);
        configContext.Setup(c => c.UnloadApps(@"C:\test.ramc")).Returns(true);
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = IpcCommands.UnloadApps,
            Arguments = @"C:\test.ramc"
        };

        var result = handler.HandleIpcMessage(message, new IpcCallerContext(@"DOMAIN\Admin", null, true, true));

        Assert.True(result.Success);
        configContext.Verify(c => c.UnloadApps(@"C:\test.ramc"), Times.Once);
    }

    // --- HandleAssociation tests ---

    private IpcAssociationHandler CreateAssociationHandler(
        Mock<IIpcCallerAuthorizer> authorizer,
        Mock<IHandlerMappingService> handlerMappingService,
        IClock? clock = null)
    {
        var associationLaunchResolver = new AssociationLaunchResolver(() => handlerMappingService.Object, authorizer.Object);
        return new IpcAssociationHandler(
            _appState.Object,
            _appLock.Object,
            new IpcUiInvoker(_uiThreadInvoker.Object, _appState.Object),
            _orchestrator.Object,
            associationLaunchResolver,
            new AssociationAccessDeniedNotifier(_trayBalloon.Object, clock ?? new SystemClock()),
            _sidNameCache.Object,
            _log.Object, _idleMonitor.Object);
    }

    [Fact]
    public void HandleAssociation_NullAssociationKey_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = null };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleAssociation_AssociationKeyNotFound_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>());
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "http" };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("http", result.ErrorMessage);
        Assert.Equal(IpcErrorCode.UnknownAssociation, result.ErrorCode);
    }

    [Fact]
    public void HandleAssociation_AppIdNotFound_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["http"] = [new HandlerMappingEntry("nonexistent")] });
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "http" };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal(IpcErrorCode.AppNotFound, result.ErrorCode);
    }

    [Fact]
    public void HandleAssociation_ValidKeyAuthorizedCaller_LaunchesAndReturnsSuccess()
    {
        // Arrange: app exists in database, mapping resolves to it, caller is authorized
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = "https",
            Arguments = "https://example.com"
        };

        // Act
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        // Assert: launch triggered with correct app and arguments
        Assert.True(result.Success);
        Assert.Equal(IpcErrorCode.None, result.ErrorCode);
        _orchestrator.Verify(o => o.Launch(app, "https://example.com", null, It.IsAny<Func<string, string, bool>?>(), ""), Times.Once);
    }

    [Fact]
    public void HandleAssociation_UnauthorizedCaller_ReturnsAccessDenied()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\Attacker", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Access denied.", result.ErrorMessage);
        Assert.Equal(IpcErrorCode.AccessDenied, result.ErrorCode);
        _trayBalloon.Verify(t => t.ShowWarning("RunFence blocked an association request because IPC access was denied."), Times.Once);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_UnauthorizedCaller_ShowsAtMostFiveWarningsPerFiveMinutes()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var handler = CreateAssociationHandler(authorizer, handlerMappingService, clock);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        for (var i = 0; i < 6; i++)
            handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\Attacker", null, false, true));

        _trayBalloon.Verify(t => t.ShowWarning("RunFence blocked an association request because IPC access was denied."), Times.Exactly(5));

        clock.Advance(TimeSpan.FromMinutes(5));

        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\Attacker", null, false, true));

        _trayBalloon.Verify(t => t.ShowWarning("RunFence blocked an association request because IPC access was denied."), Times.Exactly(6));
    }

    [Fact]
    public void HandleAssociation_PrefersAppWithExplicitIpcCallerList_WhenMultipleAppsForKey()
    {
        // mainApp has null AllowedIpcCallers (authorized via global/unrestricted)
        // extraApp has explicit AllowedIpcCallers containing the caller — should be preferred
        var mainApp = new AppEntry { Id = "main01", Name = "Main Browser", AllowedIpcCallers = null };
        var extraApp = new AppEntry { Id = "extra01", Name = "Extra Browser", AllowedIpcCallers = ["S-1-5-21-caller"] };
        _database.Apps.Add(mainApp);
        _database.Apps.Add(extraApp);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(It.IsAny<string?>(), It.IsAny<string?>(), mainApp, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(It.IsAny<string?>(), It.IsAny<string?>(), extraApp, _database, It.IsAny<bool>()))
            .Returns(true);
        // mainApp has null AllowedIpcCallers — no explicit per-app auth
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), mainApp, _database))
            .Returns(false);
        // extraApp has explicit AllowedIpcCallers — has per-app auth
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), extraApp, _database))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        // main config first, then extra config
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("main01"), new HandlerMappingEntry("extra01")] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", "S-1-5-21-caller", false, true));

        // extraApp must be launched — explicit per-app AllowedIpcCallers takes priority
        _orchestrator.Verify(o => o.Launch(extraApp, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Once);
        _orchestrator.Verify(o => o.Launch(mainApp, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_ShuttingDown_ReturnsShuttingDown()
    {
        _appState.Setup(c => c.IsShuttingDown).Returns(true);
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("shutting down", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleAssociation_NullArgumentsTemplate_PassesEmptyStringToLauncher()
    {
        // Associations intentionally do not fall back to app.ArgumentsTemplate — they use only
        // per-association templates. When ArgumentsTemplate is null, it coalesces to "" so that
        // DetermineArguments replaces DefaultArguments rather than leaving it untouched.
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(
                It.IsAny<string?>(), app, _database))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        // HandlerMappingEntry with ArgumentsTemplate = null (the default)
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
            {
                ["https"] = [new HandlerMappingEntry("browser01")] // ArgumentsTemplate = null
            });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = "https",
            Arguments = "https://example.com"
        };

        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        // The launcher must be called with associationArgsTemplate = "" (not null)
        // because null template coalesces to "" by design.
        _orchestrator.Verify(o => o.Launch(app, "https://example.com", null, It.IsAny<Func<string, string, bool>?>(), ""), Times.Once);
    }

    [Fact]
    public void HandleAssociation_ResetsIdleMonitor_OnSuccess()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        var idleMonitor = new Mock<IIdleMonitorService>();

        var handler = new IpcAssociationHandler(
            _appState.Object,
            _appLock.Object,
            new IpcUiInvoker(_uiThreadInvoker.Object, _appState.Object),
            _orchestrator.Object,
            new AssociationLaunchResolver(() => handlerMappingService.Object, authorizer.Object),
            new AssociationAccessDeniedNotifier(_trayBalloon.Object, new SystemClock()),
            _sidNameCache.Object,
            _log.Object,
            idleMonitor.Object);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Once);
    }

    [Fact]
    public void HandleAssociation_IsUnlockPolling_ReturnsBusy()
    {
        _appLock.Setup(c => c.IsUnlockPolling).Returns(true);
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Busy.", result.ErrorMessage);
        handlerMappingService.Verify(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_IsModalOpen_ReturnsBusy()
    {
        _appState.Setup(c => c.IsModalOpen).Returns(true);
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Busy.", result.ErrorMessage);
        handlerMappingService.Verify(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_Win32LogonFailure_ReturnsCredentialError()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        _orchestrator
            .Setup(o => o.Launch(app, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()))
            .Throws(new Win32Exception(ProcessLaunchNative.Win32ErrorLogonFailure));

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("credentials", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleAssociation_GeneralException_ReturnsLaunchFailed()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        _orchestrator
            .Setup(o => o.Launch(app, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Unexpected internal error"));

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal("Launch failed: InvalidOperationException", result.ErrorMessage);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void HandleAssociation_MaintenanceWarning_ReturnsSuccessWithWarningMessage()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { ["https"] = [new HandlerMappingEntry("browser01")] });

        _orchestrator
            .Setup(o => o.Launch(app, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()))
            .Returns(new LaunchExecutionResult(
                LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
                null,
                ["post-launch maintenance failed"]));

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.True(result.Success);
        Assert.Contains("post-launch maintenance failed", result.WarningMessage, StringComparison.Ordinal);
    }

    // --- Path prefix filtering tests ---

    public static IEnumerable<object?[]> PrefixMatchCases =>
    [
        // No prefixes anywhere → success
        [null, null, false, "any.pdf", IpcErrorCode.None],
        // App-level prefix matches (Add mode)
        [new[] { @"C:\Work\" }, null, false, @"C:\Work\report.pdf", IpcErrorCode.None],
        // App-level prefix mismatch → PathPrefixMismatch
        [new[] { @"C:\Work\" }, null, false, @"C:\Other\doc.pdf", IpcErrorCode.PathPrefixMismatch],
        // Association adds prefix — union match
        [new[] { @"C:\Work\" }, new[] { @"C:\Docs\" }, false, @"C:\Docs\file.pdf", IpcErrorCode.None],
        // Replace mode ignores app prefixes
        [new[] { @"C:\Work\" }, new[] { @"C:\Other\" }, true, @"C:\Other\doc.pdf", IpcErrorCode.None],
        // Replace mode with empty entry = catch-all
        [new[] { @"C:\Work\" }, Array.Empty<string>(), true, @"C:\Other\doc.pdf", IpcErrorCode.None],
        // Case-insensitive
        [new[] { @"C:\WORK\" }, null, false, @"C:\work\doc.pdf", IpcErrorCode.None],
        // URL prefix (per-association level)
        [null, new[] { "https://internal.company.com/" }, false, "https://internal.company.com/page", IpcErrorCode.None],
        // Null arguments + prefix exists → PathPrefixMismatch
        [new[] { @"C:\Work\" }, null, false, null, IpcErrorCode.PathPrefixMismatch],
        // Entry-only prefix, no app prefix (Add mode) — match
        [null, new[] { @"C:\Work\" }, false, @"C:\Work\file.pdf", IpcErrorCode.None],
        // Entry-only prefix, no app prefix (Add mode) — mismatch
        [null, new[] { @"C:\Work\" }, false, @"C:\Other\file.pdf", IpcErrorCode.PathPrefixMismatch],
        // Boundary separator: C:\Work must NOT match C:\WorkEvil\file.pdf
        [new[] { @"C:\Work" }, null, false, @"C:\WorkEvil\file.pdf", IpcErrorCode.PathPrefixMismatch],
        // Boundary separator: C:\Work must match C:\Work\report.pdf
        [new[] { @"C:\Work" }, null, false, @"C:\Work\report.pdf", IpcErrorCode.None]
    ];

    [Theory]
    [MemberData(nameof(PrefixMatchCases))]
    public void HandleAssociation_PrefixFiltering(string[]? appPrefixes, string[]? entryPrefixes,
        bool replacePrefixes, string? arguments, IpcErrorCode expectedError)
    {
        // Arrange
        var app = new AppEntry
        {
            Id = "browser01",
            Name = "RunFence Browser",
            PathPrefixes = appPrefixes?.Length > 0 ? [..appPrefixes] : null
        };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), app, _database))
            .Returns(false);

        List<string>? entryPathPrefixes = entryPrefixes?.Length > 0 ? [..entryPrefixes] : null;
        var entry = new HandlerMappingEntry("browser01", null, entryPathPrefixes, replacePrefixes);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>> { [".pdf"] = [entry] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = arguments
        };

        // Act
        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        // Assert
        if (expectedError == IpcErrorCode.None)
        {
            Assert.True(result.Success);
        }
        else
        {
            Assert.False(result.Success);
            Assert.Equal(expectedError, result.ErrorCode);
        }
    }

    [Fact]
    public void HandleAssociation_UnauthorizedCaller_AccessDeniedRegardlessOfPrefix()
    {
        // Even when the prefix would match, unauthorized caller gets AccessDenied (not PathPrefixMismatch)
        var app = new AppEntry
        {
            Id = "browser01",
            Name = "RunFence Browser",
            PathPrefixes = [$@"C:\Work\"]
        };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
                { [".pdf"] = [new HandlerMappingEntry("browser01")] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = @"C:\Work\report.pdf"
        };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\Attacker", null, false, true));

        Assert.False(result.Success);
        Assert.Equal(IpcErrorCode.AccessDenied, result.ErrorCode);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_AllEntriesFailPrefixCheck_ReturnsPathPrefixMismatch()
    {
        // Two authorized entries — both fail effective prefix filter → PathPrefixMismatch
        var appA = new AppEntry
        {
            Id = "appA",
            Name = "App A",
            PathPrefixes = [$@"C:\Restricted\"]
        };
        var appB = new AppEntry
        {
            Id = "appB",
            Name = "App B",
            PathPrefixes = [$@"C:\Other\"]
        };
        _database.Apps.Add(appA);
        _database.Apps.Add(appB);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appA, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appB, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), It.IsAny<AppEntry>(), _database))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
            {
                [".pdf"] = [new HandlerMappingEntry("appA"), new HandlerMappingEntry("appB")]
            });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = @"C:\Elsewhere\file.pdf"
        };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal(IpcErrorCode.PathPrefixMismatch, result.ErrorCode);
    }

    [Fact]
    public void HandleAssociation_ExplicitAuthWinsAmongPrefixMatchingCandidates()
    {
        // App A has explicit per-app auth + matching prefix; App B is catch-all → App A launched
        var appA = new AppEntry { Id = "appA", Name = "App A" };
        var appB = new AppEntry { Id = "appB", Name = "App B" };
        _database.Apps.Add(appA);
        _database.Apps.Add(appB);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appA, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appB, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), appA, _database))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), appB, _database))
            .Returns(false);

        var entryA = new HandlerMappingEntry("appA", "template-a", [$@"C:\Work\"], false);
        var entryB = new HandlerMappingEntry("appB", "template-b");

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
                { [".pdf"] = [entryB, entryA] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = @"C:\Work\report.pdf"
        };

        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", "S-1-5-21-caller", false, true));

        _orchestrator.Verify(o => o.Launch(appA, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Once);
        _orchestrator.Verify(o => o.Launch(appB, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_MixedUnauthorizedAndPrefixFail_ReturnsPathPrefixMismatch()
    {
        // Entry A: authorized + prefix fails; Entry B: unauthorized + prefix would match
        // Expected: PathPrefixMismatch (because at least one entry was authorized)
        var appA = new AppEntry
        {
            Id = "appA",
            Name = "App A",
            PathPrefixes = [$@"C:\Restricted\"]
        };
        var appB = new AppEntry { Id = "appB", Name = "App B" };
        _database.Apps.Add(appA);
        _database.Apps.Add(appB);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appA, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appB, _database, It.IsAny<bool>()))
            .Returns(false);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), It.IsAny<AppEntry>(), _database))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
            {
                [".pdf"] = [new HandlerMappingEntry("appA"), new HandlerMappingEntry("appB")]
            });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = @"C:\Elsewhere\file.pdf"
        };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal(IpcErrorCode.PathPrefixMismatch, result.ErrorCode);
    }

    [Fact]
    public void HandleAssociation_MatchedEntryTemplateUsed()
    {
        // Two apps for same key with different ArgumentsTemplate values and different prefix configs.
        // Argument matches App A's prefix only. Assert launcher called with App A's template.
        var appA = new AppEntry
        {
            Id = "appA",
            Name = "App A",
            PathPrefixes = [$@"C:\Work\"]
        };
        var appB = new AppEntry
        {
            Id = "appB",
            Name = "App B",
            PathPrefixes = [$@"C:\Docs\"]
        };
        _database.Apps.Add(appA);
        _database.Apps.Add(appB);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appA, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), appB, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), It.IsAny<AppEntry>(), _database))
            .Returns(false);

        var entryA = new HandlerMappingEntry("appA", "template-a");
        var entryB = new HandlerMappingEntry("appB", "template-b");

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
                { [".pdf"] = [entryA, entryB] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".pdf",
            Arguments = @"C:\Work\report.pdf"
        };

        handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        _orchestrator.Verify(o => o.Launch(appA, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), "template-a"), Times.Once);
        _orchestrator.Verify(o => o.Launch(appB, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_PathTraversalBypassBlocked()
    {
        // Prefix C:\Work\, argument with ".." traversal → PathPrefixMismatch after canonicalization
        var app = new AppEntry
        {
            Id = "browser01",
            Name = "RunFence Browser",
            PathPrefixes = [$@"C:\Work\"]
        };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);
        authorizer.Setup(a => a.HasExplicitPerAppAuthorization(It.IsAny<string?>(), app, _database))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
                { [".txt"] = [new HandlerMappingEntry("browser01")] });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = ".txt",
            Arguments = @"C:\Work\..\Secrets\file.txt"
        };

        var result = handler.HandleAssociation(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Equal(IpcErrorCode.PathPrefixMismatch, result.ErrorCode);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<string?>()), Times.Never);
    }

    // --- HandleAssociation routing through IpcMessageHandler.HandleIpcMessage ---

    [Fact]
    public void HandleIpcMessage_HandleAssociationCommand_RoutesToAssociationHandlerAndReturnsSuccess()
    {
        // Arrange: app registered in database, handler mapping resolves to it, caller authorized
        var app = new AppEntry { Id = "browser99", Name = "Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database, It.IsAny<bool>()))
            .Returns(true);
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(_database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>
                { ["https"] = [new HandlerMappingEntry("browser99")] });

        var messageHandler = CreateHandler(associationHandler: CreateAssociationHandler(authorizer, handlerMappingService));

        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = "https",
            Arguments = "https://example.com"
        };

        // Act
        var result = messageHandler.HandleIpcMessage(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        // Assert — routed through HandleIpcMessage dispatcher to association handler
        Assert.True(result.Success);
        _orchestrator.Verify(o => o.Launch(app, "https://example.com", null,
            It.IsAny<Func<string, string, bool>?>(), ""), Times.Once);
    }

    [Fact]
    public void HandleIpcMessage_HandleAssociationCommand_NullAssociationHandler_ReturnsNotAvailable()
    {
        // _handler constructed in ctor uses null associationHandler
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = _handler.HandleIpcMessage(msg, new IpcCallerContext(@"DOMAIN\User", null, false, true));

        Assert.False(result.Success);
        Assert.Contains("not available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class MockTrayWarningSink(ITrayBalloonService trayBalloonService) : ITrayWarningSink
    {
        public void ShowWarning(string text) => trayBalloonService.ShowWarning(text);
    }
}

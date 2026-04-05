using System.ComponentModel;
using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class IpcMessageHandlerTests
{
    private readonly Mock<IAppStateProvider> _appState;
    private readonly Mock<IAppLockControl> _appLock;
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker;
    private readonly Mock<IAppLaunchOrchestrator> _orchestrator;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IIdleMonitorService> _idleMonitor;
    private readonly IpcMessageHandler _handler;
    private readonly AppDatabase _database;

    public IpcMessageHandlerTests()
    {
        _database = new AppDatabase();
        _appState = new Mock<IAppStateProvider>();
        _appLock = new Mock<IAppLockControl>();
        _uiThreadInvoker = new Mock<IUiThreadInvoker>();

        _appState.Setup(c => c.Database).Returns(_database);
        _appState.Setup(c => c.IsShuttingDown).Returns(false);
        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>())).Callback<Action>(a => a());
        _uiThreadInvoker.Setup(c => c.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());

        _orchestrator = new Mock<IAppLaunchOrchestrator>();
        _log = new Mock<ILoggingService>();
        _idleMonitor = new Mock<IIdleMonitorService>();

        _handler = CreateHandler();
    }

    private IpcMessageHandler CreateHandler(
        IIdleMonitorService? idleMonitor = null,
        IConfigManagementContext? configContext = null,
        IRunAsFlowHandler? runAsFlowHandler = null)
    {
        var resolvedIdleMonitor = idleMonitor ?? _idleMonitor.Object;
        var sidResolver = new Mock<ISidResolver>();
        var authorizer = new IpcCallerAuthorizer(_log.Object, sidResolver.Object);
        var launchHandler = new IpcLaunchHandler(
            _appState.Object, _appLock.Object, _uiThreadInvoker.Object,
            _orchestrator.Object, authorizer, _log.Object,
            resolvedIdleMonitor, runAsFlowHandler);
        var openFolderHandler = new IpcOpenFolderHandler(
            _appState.Object, _appLock.Object, _uiThreadInvoker.Object,
            directoryValidator: null, _log.Object, new ShellFolderOpener());
        return new IpcMessageHandler(
            _appState.Object, _appLock.Object, _uiThreadInvoker.Object,
            _log.Object, launchHandler, openFolderHandler,
            idleMonitor: resolvedIdleMonitor, configContext: configContext);
    }

    private IpcMessageHandler CreateHandlerWithConfig(Mock<IConfigManagementContext> configContext)
        => CreateHandler(configContext: configContext.Object);

    [Fact]
    public void Ping_ReturnsSuccess()
    {
        var message = new IpcMessage { Command = IpcCommands.Ping };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Launch_NullOrEmptyAppId_ReturnsError(string? appId)
    {
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = appId };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Equal("AppId is required.", result.ErrorMessage);
    }

    [Fact]
    public void Launch_UnknownAppId_ReturnsNotFound()
    {
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "XXXXX" };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Denied", null, false);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.True(result.Success);
        _orchestrator.Verify(o => o.Launch(app, message.Arguments, message.WorkingDirectory), Times.Once);
    }

    [Fact]
    public void Launch_ShuttingDown_ReturnsError()
    {
        _appState.Setup(c => c.IsShuttingDown).Returns(true);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Equal("Application is shutting down.", result.ErrorMessage);
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var message = new IpcMessage { Command = "FooBar" };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Equal("Unknown command: FooBar", result.ErrorMessage);
    }

    [Fact]
    public void Launch_NullArguments_PassesNullToOrchestrator()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01", Arguments = null };

        _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        _orchestrator.Verify(o => o.Launch(app, null, null), Times.Once);
    }

    // --- Shutdown command tests ---

    [Fact]
    public void Shutdown_NonAdmin_ReturnsAccessDenied()
    {
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Contains("Admin required", result.ErrorMessage);
    }

    [Fact]
    public void Shutdown_Admin_ReturnsSuccessAndSchedulesExit()
    {
        _appState.Setup(c => c.IsOperationInProgress).Returns(false);
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.True(result.Success);
        _uiThreadInvoker.Verify(c => c.BeginInvoke(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void Shutdown_Admin_OperationInProgress_ReturnsError()
    {
        _appState.Setup(c => c.IsOperationInProgress).Returns(true);
        var message = new IpcMessage { Command = IpcCommands.Shutdown };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.False(result.Success);
        Assert.Contains("Operation in progress", result.ErrorMessage);
    }

    // --- Unlock command tests ---

    [Fact]
    public void Unlock_NonAdmin_ReturnsAccessDenied()
    {
        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Contains("Admin required", result.ErrorMessage);
    }

    [Fact]
    public void Unlock_Admin_ReturnsSuccess()
    {
        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.True(result.Success);
        _appLock.Verify(c => c.Unlock(), Times.Once);
    }

    [Fact]
    public void Unlock_Admin_InvokeThrows_ReturnsError()
    {
        _uiThreadInvoker.Setup(c => c.Invoke(It.IsAny<Action>()))
            .Throws(new ObjectDisposedException("MainForm"));

        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.False(result.Success);
        Assert.Contains("Unlock failed", result.ErrorMessage);
    }

    [Fact]
    public void Unlock_Admin_StillLockedAfterUnlock_ReturnsCancelled()
    {
        // Simulate Unlock() being called but IsLocked remaining true (e.g. PIN cancelled)
        _appLock.Setup(c => c.IsLocked).Returns(true);

        var message = new IpcMessage { Command = IpcCommands.Unlock };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.False(result.Success);
        Assert.Equal("Unlock cancelled.", result.ErrorMessage);
        _appLock.Verify(c => c.Unlock(), Times.Once);
    }

    [Fact]
    public void Launch_Win32LogonFailure_ReturnsCredentialError()
    {
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        _orchestrator
            .Setup(o => o.Launch(app, It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new Win32Exception(ProcessLaunchNative.Win32ErrorLogonFailure));

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Contains("credentials are incorrect", result.ErrorMessage);
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
        handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Once);
    }

    [Fact]
    public void Launch_NoIdleMonitor_DoesNotThrow()
    {
        // Default handler has no idle monitor (null)
        var app = new AppEntry { Id = "app01", Name = "TestApp" };
        _database.Apps.Add(app);

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app01" };
        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Equal("Busy.", result.ErrorMessage);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
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

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

        Assert.False(result.Success);
        Assert.Equal("Run As not available.", result.ErrorMessage);
    }

    // --- Null config context returns "not available" (T7b) ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_NullConfigContext_ReturnsConfigManagementNotAvailable(string command)
    {
        // _handler has no configContext (null by default in constructor)
        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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

        var result = _handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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

        var result = handler.HandleIpcMessage(message, @"DOMAIN\User", null, false);

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

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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
            .Returns((false, "Decryption failed."));
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = IpcCommands.LoadApps,
            Arguments = @"C:\test.ramc"
        };

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.False(result.Success);
        Assert.Equal("Decryption failed.", result.ErrorMessage);
    }

    // --- LoadApps/UnloadApps with config context ---

    [Theory]
    [InlineData(IpcCommands.LoadApps)]
    [InlineData(IpcCommands.UnloadApps)]
    public void ConfigOp_UnlockFails_ReturnsUnlockRequired(string command)
    {
        var configContext = new Mock<IConfigManagementContext>();
        // IsLocked remains true after Unlock() → cancelled
        _appLock.Setup(c => c.IsLocked).Returns(true);
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage { Command = command, Arguments = @"C:\test.ramc" };

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.False(result.Success);
        Assert.Equal("Unlock required.", result.ErrorMessage);
    }

    [Fact]
    public void LoadApps_Success_DelegatesToConfigContext()
    {
        var configContext = new Mock<IConfigManagementContext>();
        _appLock.Setup(c => c.IsLocked).Returns(false);
        configContext.Setup(c => c.LoadApps(@"C:\test.ramc"))
            .Returns((true, null));
        var handler = CreateHandlerWithConfig(configContext);

        var message = new IpcMessage
        {
            Command = IpcCommands.LoadApps,
            Arguments = @"C:\test.ramc"
        };

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

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

        var result = handler.HandleIpcMessage(message, @"DOMAIN\Admin", null, true);

        Assert.True(result.Success);
        configContext.Verify(c => c.UnloadApps(@"C:\test.ramc"), Times.Once);
    }

    // --- HandleAssociation tests ---

    private IpcAssociationHandler CreateAssociationHandler(
        Mock<IIpcCallerAuthorizer> authorizer,
        Mock<IHandlerMappingService> handlerMappingService)
        => new(_appState.Object, _appLock.Object, _uiThreadInvoker.Object,
            _orchestrator.Object, authorizer.Object, handlerMappingService.Object, _log.Object,
            _idleMonitor.Object);

    [Fact]
    public void HandleAssociation_NullAssociationKey_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = null };
        var result = handler.HandleAssociation(msg, @"DOMAIN\User", null);

        Assert.False(result.Success);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleAssociation_AssociationKeyNotFound_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, string>());
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "http" };
        var result = handler.HandleAssociation(msg, @"DOMAIN\User", null);

        Assert.False(result.Success);
        Assert.Contains("http", result.ErrorMessage);
    }

    [Fact]
    public void HandleAssociation_AppIdNotFound_ReturnsError()
    {
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, string> { ["http"] = "nonexistent" });
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "http" };
        var result = handler.HandleAssociation(msg, @"DOMAIN\User", null);

        Assert.False(result.Success);
    }

    [Fact]
    public void HandleAssociation_ValidKeyAuthorizedCaller_LaunchesAndReturnsSuccess()
    {
        // Arrange: app exists in database, mapping resolves to it, caller is authorized
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(_database))
            .Returns(new Dictionary<string, string> { ["https"] = "browser01" });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = "https",
            Arguments = "https://example.com"
        };

        // Act
        var result = handler.HandleAssociation(msg, @"DOMAIN\User", null);

        // Assert: launch triggered with correct app and arguments
        Assert.True(result.Success);
        _orchestrator.Verify(o => o.Launch(app, "https://example.com", null), Times.Once);
    }

    [Fact]
    public void HandleAssociation_UnauthorizedCaller_ReturnsAccessDenied()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database))
            .Returns(false);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(_database))
            .Returns(new Dictionary<string, string> { ["https"] = "browser01" });

        var handler = CreateAssociationHandler(authorizer, handlerMappingService);
        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };

        var result = handler.HandleAssociation(msg, @"DOMAIN\Attacker", null);

        Assert.False(result.Success);
        Assert.Equal("Access denied.", result.ErrorMessage);
        _orchestrator.Verify(o => o.Launch(It.IsAny<AppEntry>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void HandleAssociation_ShuttingDown_ReturnsShuttingDown()
    {
        _appState.Setup(c => c.IsShuttingDown).Returns(true);
        var authorizer = new Mock<IIpcCallerAuthorizer>();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var handler = CreateAssociationHandler(authorizer, handlerMappingService);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        var result = handler.HandleAssociation(msg, @"DOMAIN\User", null);

        Assert.False(result.Success);
        Assert.Contains("shutting down", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleAssociation_ResetsIdleMonitor_OnSuccess()
    {
        var app = new AppEntry { Id = "browser01", Name = "RunFence Browser" };
        _database.Apps.Add(app);

        var authorizer = new Mock<IIpcCallerAuthorizer>();
        authorizer.Setup(a => a.IsCallerAuthorizedForAssociation(
                It.IsAny<string?>(), It.IsAny<string?>(), app, _database))
            .Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(_database))
            .Returns(new Dictionary<string, string> { ["https"] = "browser01" });

        var idleMonitor = new Mock<IIdleMonitorService>();

        var handler = new IpcAssociationHandler(
            _appState.Object, _appLock.Object, _uiThreadInvoker.Object,
            _orchestrator.Object, authorizer.Object, handlerMappingService.Object,
            _log.Object, idleMonitor.Object);

        var msg = new IpcMessage { Command = IpcCommands.HandleAssociation, Association = "https" };
        handler.HandleAssociation(msg, @"DOMAIN\User", null);

        idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Once);
    }
}
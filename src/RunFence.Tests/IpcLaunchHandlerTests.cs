using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="IpcLaunchHandler"/>, focusing on the RunAs path and spoofing protection.
/// </summary>
public class IpcLaunchHandlerTests
{
    private const string AllowedSid = "S-1-5-21-100-200-300-1001";

    private static AppDatabase DbWithIpcCaller(string sid) => new()
    {
        Accounts = [new AccountEntry { Sid = sid, IsIpcCaller = true }]
    };

    // --- RunAs path: spoofing protection ---

    [Fact]
    public void HandleLaunch_RunAsPath_ImpersonationFailed_GlobalListNonEmpty_ReturnsDenied()
    {
        // Arrange: global IPC caller list is non-empty (spoofing protection is active).
        // Impersonation failed: IdentityFromImpersonation = false, CallerSid = null.
        // A path-based AppId triggers the RunAs flow in IpcLaunchHandler.
        // The global IPC authorization check in RunAsFlowHandler.HandleRunAs must reject the
        // request because the caller has no verified SID and identity was not from impersonation.

        var log = new Mock<ILoggingService>().Object;
        var sidResolver = new Mock<ISidResolver>().Object;
        var authorizer = new IpcCallerAuthorizer(log, sidResolver);

        var appState = new Mock<IAppStateProvider>();
        appState.Setup(a => a.Database).Returns(DbWithIpcCaller(AllowedSid));
        appState.Setup(a => a.IsShuttingDown).Returns(false);
        appState.Setup(a => a.IsModalOpen).Returns(false);
        appState.Setup(a => a.IsOperationInProgress).Returns(false);

        var appLock = new Mock<IAppLockControl>();
        appLock.Setup(a => a.IsUnlockPolling).Returns(false);

        var uiInvoker = new Mock<IUiThreadInvoker>();
        var idleMonitor = new Mock<IIdleMonitorService>();
        var stopwatch = new Mock<IStopwatchProvider>();

        // These deps are never reached when authorization fails — authorization check is first
        var runAsFlowHandler = new RunAsFlowHandler(
            appState.Object,
            appLock.Object,
            uiInvoker.Object,
            log,
            null!,    // RunAsDialogPresenter — not reached
            null!,    // RunAsResultProcessor — not reached
            new RunAsDosProtection(stopwatch.Object),
            authorizer,
            idleMonitor.Object,
            null!);   // RunAsShortcutHelper — not reached

        var ipcUiInvoker = new IpcUiInvoker(uiInvoker.Object, appState.Object);

        var handler = new IpcLaunchHandler(
            appState.Object,
            appLock.Object,
            ipcUiInvoker,
            null!,    // IAppEntryLauncher — not reached on RunAs path
            authorizer,
            null!,    // ISidNameCacheService — not reached on RunAs path
            log,
            idleMonitor.Object,
            runAsFlowHandler);

        // A path-based AppId triggers the RunAs flow (contains path separator)
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = @"C:\SomeApp.exe" };

        // Context: impersonation failed, no SID, identity not from impersonation
        var failedContext = new IpcCallerContext(
            CallerIdentity: @"DOMAIN\SomeUser",
            CallerSid: null,
            IsAdmin: false,
            IdentityFromImpersonation: false);

        // Act
        var response = handler.HandleLaunch(message, failedContext);

        // Assert: denied because spoofing protection in AuthorizeAgainstList rejects
        // callers whose identity was not verified via pipe impersonation and have no SID
        Assert.False(response.Success);
        Assert.Equal("Access denied.", response.ErrorMessage);
    }
}

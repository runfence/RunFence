using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupIpcBootstrapperTests
{
    [Fact]
    public async Task SetupIpcOnHandleCreated_CapturesEnforcementSnapshotOnUiThread()
    {
        var liveDatabase = new AppDatabase();
        liveDatabase.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-100-200-300-4000" });

        var session = new SessionContext
{
            Database = liveDatabase,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new Mock<ISessionProvider>();
        var sessionThreadId = 0;
        sessionProvider.Setup(s => s.GetSession())
            .Callback(() => sessionThreadId = Environment.CurrentManagedThreadId)
            .Returns(session);

        AppDatabase? enforcedDatabase = null;
        var enforced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firewall = new Mock<IFirewallEnforcementOrchestrator>();
        firewall.Setup(f => f.EnforceAll(It.IsAny<AppDatabase>()))
            .Callback<AppDatabase>(database =>
            {
                enforcedDatabase = database;
                enforced.SetResult();
            })
            .Returns(new EnforceAllResult([]));

        var ipcServer = new Mock<IIpcServerService>();
        ipcServer.Setup(s => s.Start(It.IsAny<Func<IpcMessage, IpcCallerContext, IpcResponse>>()));

        var host = new FakeStartupIpcHost();
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var portRangeChecker = new DynamicPortRangeChecker(
            Mock.Of<ILoggingService>(),
            Mock.Of<IUserConfirmationService>(),
            Mock.Of<INetshCommandRunner>());

        var bootstrapper = new StartupIpcBootstrapper(
            host,
            ipcServer.Object,
            Mock.Of<IIpcMessageHandler>(),
            firewall.Object,
            portRangeChecker,
            new UiThreadDatabaseAccessor(
                new LambdaDatabaseProvider(() => sessionProvider.Object.GetSession().Database),
                () => uiInvoker),
            Mock.Of<ILoggingService>());

        bootstrapper.SetupIpcOnHandleCreated();

        await Task.Run(host.RaiseHandleCreated);
        await enforced.Task;

        Assert.Equal(1, host.StartupCompleteCallCount);
        Assert.Equal(uiInvoker.ThreadId, sessionThreadId);
        Assert.NotNull(enforcedDatabase);
        Assert.NotSame(liveDatabase, enforcedDatabase);
        ipcServer.Verify(s => s.Start(It.IsAny<Func<IpcMessage, IpcCallerContext, IpcResponse>>()), Times.Once);
    }

    private sealed class FakeStartupIpcHost : IStartupIpcHost
    {
        public event EventHandler? HandleCreated;
        public event FormClosingEventHandler? FormClosing;

        public int StartupCompleteCallCount { get; private set; }

        public void BeginInvokeOnUiThread(Action action) => action();

        public void SetStartupComplete() => StartupCompleteCallCount++;

        public void RaiseHandleCreated() => HandleCreated?.Invoke(this, EventArgs.Empty);

        public void RaiseFormClosing(FormClosingEventArgs args) => FormClosing?.Invoke(this, args);
    }
}

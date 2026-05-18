using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountProcessTimerManagerTests
{
    private const string TestSid = "S-1-5-21-123-456-789-1001";

    [Fact]
    public void RunProcessRefreshAsync_DisposeDuringRefresh_DoesNotRestartDisposedTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var grid = CreateAccountsGrid();
            form.Controls.Add(grid);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var refreshStarted = new TaskCompletionSource<bool>();
            var refreshCalls = 0;
            var togglePrimed = 0;
            var processList = new Mock<IProcessListService>();
            processList.Setup(s => s.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, ct) =>
                {
                    if (Interlocked.CompareExchange(ref togglePrimed, 1, 0) == 0)
                    {
                        Interlocked.Increment(ref refreshCalls);
                        return [new ProcessInfo(123, "proc.exe", "proc.exe", null)];
                    }

                    Interlocked.Increment(ref refreshCalls);
                    refreshStarted.SetResult(true);
                    ct.WaitHandle.WaitOne();
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    return [];
                });
            processList.Setup(s => s.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns([]);

            var expander = new AccountGridProcessExpander(processList.Object, new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);
            expander.Toggle(TestSid);

            var timerFactory = new ManualUiTimerFactory();
            using var manager = new AccountProcessTimerManager(Mock.Of<ILoggingService>(), timerFactory);
            manager.Initialize(grid, expander, () => false);
            manager.Start(() => true);
            var refreshTimer = timerFactory.Timers[0];

            refreshTimer.Fire();
            StaTestHelper.PumpUntil(() => refreshStarted.Task.IsCompleted);
            manager.Dispose();
            Assert.Equal(2, refreshCalls);
            Assert.Equal(1, refreshTimer.StartCallCount);
            Assert.Equal(1, refreshTimer.DisposeCallCount);
        });
    }

    [Fact]
    public void RunProcessCheckAsync_DisposeDuringCheck_DoesNotRestartDisposedTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var grid = CreateAccountsGrid();
            form.Controls.Add(grid);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var checkStarted = new TaskCompletionSource<bool>();
            var checkCalls = 0;
            var processList = new Mock<IProcessListService>();
            processList.Setup(s => s.GetProcessesForSid(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns([]);
            processList.Setup(s => s.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns<IEnumerable<string>, CancellationToken>((_, ct) =>
                {
                    Interlocked.Increment(ref checkCalls);
                    checkStarted.SetResult(true);
                    ct.WaitHandle.WaitOne();
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    return [];
                });

            var expander = new AccountGridProcessExpander(processList.Object, new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var timerFactory = new ManualUiTimerFactory();
            using var manager = new AccountProcessTimerManager(Mock.Of<ILoggingService>(), timerFactory);
            manager.Initialize(grid, expander, () => false);
            manager.Start(() => true);
            var checkTimer = timerFactory.Timers[1];

            checkTimer.Fire();
            StaTestHelper.PumpUntil(() => checkStarted.Task.IsCompleted);
            manager.Dispose();
            Assert.Equal(1, checkCalls);
            Assert.Equal(1, checkTimer.StartCallCount);
            Assert.Equal(1, checkTimer.DisposeCallCount);
        });
    }

    [Fact]
    public void RunProcessRefreshAsync_WhenFetchThrows_DoesNotFaultAndRestartsTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var grid = CreateAccountsGrid();
            form.Controls.Add(grid);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var refreshCalls = 0;
            var togglePrimed = 0;
            var processList = new Mock<IProcessListService>();
            processList.Setup(s => s.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    var currentCall = Interlocked.Increment(ref refreshCalls);
                    if (Interlocked.CompareExchange(ref togglePrimed, 1, 0) == 0)
                        return [new ProcessInfo(123, "proc.exe", "proc.exe", null)];

                    throw new InvalidOperationException("refresh failed");
                });
            processList.Setup(s => s.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns([]);

            var expander = new AccountGridProcessExpander(processList.Object, new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);
            expander.Toggle(TestSid);

            var timerFactory = new ManualUiTimerFactory();
            using var manager = new AccountProcessTimerManager(Mock.Of<ILoggingService>(), timerFactory);
            manager.Initialize(grid, expander, () => false);
            manager.Start(() => true);
            var refreshTimer = timerFactory.Timers[0];

            refreshTimer.Fire();
            StaTestHelper.PumpUntil(() => refreshCalls >= 2);
            StaTestHelper.PumpUntil(() => refreshTimer.StartCallCount >= 2);
            Assert.True(refreshTimer.Enabled);
        });
    }

    [Fact]
    public void RunProcessCheckAsync_WhenFetchThrows_DoesNotFaultAndRestartsTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var grid = CreateAccountsGrid();
            form.Controls.Add(grid);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var checkCalls = 0;
            var processList = new Mock<IProcessListService>();
            processList.Setup(s => s.GetProcessesForSid(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns([]);
            processList.Setup(s => s.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns<IEnumerable<string>, CancellationToken>((_, _) =>
                {
                    Interlocked.Increment(ref checkCalls);
                    throw new InvalidOperationException("check failed");
                });

            var expander = new AccountGridProcessExpander(processList.Object, new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var timerFactory = new ManualUiTimerFactory();
            using var manager = new AccountProcessTimerManager(Mock.Of<ILoggingService>(), timerFactory);
            manager.Initialize(grid, expander, () => false);
            manager.Start(() => true);
            var checkTimer = timerFactory.Timers[1];

            checkTimer.Fire();
            StaTestHelper.PumpUntil(() => checkCalls >= 1);
            StaTestHelper.PumpUntil(() => checkTimer.StartCallCount >= 2);
            Assert.True(checkTimer.Enabled);
        });
    }

    [Fact]
    public void KillAllProcessesAsync_UsesBackgroundDispatch()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var grid = new DataGridView();
            form.Controls.Add(grid);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var killStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseKill = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processTermination = new Mock<IProcessTerminationService>();
            var uiThreadId = Environment.CurrentManagedThreadId;
            var killThreadId = -1;
            processTermination.Setup(s => s.KillProcesses(TestSid))
                .Returns(() =>
                {
                    killThreadId = Environment.CurrentManagedThreadId;
                    killStarted.TrySetResult(true);
                    releaseKill.Task.Wait();
                    return new ProcessKillResult(1, 0);
                });

            var messageBox = new Mock<IAccountMessageBoxService>();
            messageBox.Setup(m => m.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), "Kill All Processes",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2))
                .Returns(DialogResult.Yes);
            messageBox.Setup(m => m.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), "Kill All Processes",
                    MessageBoxButtons.OK, It.IsAny<MessageBoxIcon>(), MessageBoxDefaultButton.Button1))
                .Returns(DialogResult.OK);

            var handler = new AccountContextMenuHandler(
                CreateMenuStateConfigurator(),
                CreateSessionProvider().Object,
                processTermination.Object,
                messageBox.Object);
            handler.Initialize(grid, processDisplayManager: null);

            var task = handler.KillAllProcessesAsync(new AccountRow(null, "test", TestSid, false));

            Assert.False(task.IsCompleted);

            StaTestHelper.PumpUntil(() => killStarted.Task.IsCompleted, timeout: TimeSpan.FromSeconds(5),
                timeoutMessage: "Kill-all dispatch did not start background kill workflow.");
            Assert.NotEqual(uiThreadId, Volatile.Read(ref killThreadId));
            releaseKill.TrySetResult(true);
            StaTestHelper.PumpUntil(() => task.IsCompleted);
            if (!task.IsCompletedSuccessfully)
            {
                if (task.Exception != null)
                    throw task.Exception;

                throw new InvalidOperationException("Kill-all task did not complete successfully.");
            }

            processTermination.Verify(s => s.KillProcesses(TestSid), Times.Once);
        });
    }

    private static DataGridView CreateAccountsGrid()
    {
        var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Import" });
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Credential" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Logon" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colAllowInternet" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Apps" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProfilePath" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SID" });
        var idx = grid.Rows.Add(false, AccountGridHelper.EmptyIcon, "User", false, false, "", "", TestSid);
        grid.Rows[idx].Tag = new AccountRow(null, "User", TestSid, false);
        return grid;
    }

    private static AccountMenuStateConfigurator CreateMenuStateConfigurator()
    {
        var sessionProvider = CreateSessionProvider();
        return new AccountMenuStateConfigurator(
            Mock.Of<IWindowsAccountQueryService>(),
            sessionProvider.Object,
            new AccountToolResolver(Mock.Of<IProfilePathResolver>()),
            new PackageInstallService(
                Mock.Of<IPackageInstallLauncher>(),
                Mock.Of<IPackageInstallScriptStore>(),
                new AccountToolResolver(Mock.Of<IProfilePathResolver>())));
    }

    private static Mock<ISessionProvider> CreateSessionProvider()
    {
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        });
        return sessionProvider;
    }
}

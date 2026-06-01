using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class AccountGridProcessExpanderTests
{
    private const string TestSid = "S-1-5-21-123-456-789-1001";
    private const string OtherSid = "S-1-5-21-123-456-789-1002";

    [Fact]
    public void ToggleAsync_ExpandingFetchesProcessesOffUiThread()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parentRow = AddParentRow(grid);
            var uiThreadId = Environment.CurrentManagedThreadId;
            var fetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fetchThreadId = -1;
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    fetchThreadId = Environment.CurrentManagedThreadId;
                    fetchStarted.TrySetResult(true);
                    releaseFetch.Task.Wait();
                    return [new ProcessInfo(123, @"C:\Apps\test.exe", "\"C:\\Apps\\test.exe\"")];
                });

            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var toggleTask = expander.ToggleAsync(TestSid);

            Assert.False(toggleTask.IsCompleted);
            StaTestHelper.PumpUntil(() => fetchStarted.Task.IsCompleted);
            Assert.NotEqual(uiThreadId, Volatile.Read(ref fetchThreadId));
            Assert.Equal(1, grid.Rows.Count);

            releaseFetch.SetResult(true);
            StaTestHelper.PumpUntil(() => toggleTask.IsCompleted);
            if (!toggleTask.IsCompletedSuccessfully)
            {
                if (toggleTask.Exception != null)
                    throw toggleTask.Exception;

                throw new InvalidOperationException("Process expansion did not complete successfully.");
            }

            Assert.Same(parentRow, grid.Rows[0]);
            var processRow = Assert.IsType<ProcessRow>(grid.Rows[1].Tag);
            Assert.Equal(123, processRow.Process.Pid);
        });
    }

    [Fact]
    public void ToggleAsync_CollapsingDoesNotFetchProcessesAgain()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var grid = CreateGrid();
            AddParentRow(grid);
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns([new ProcessInfo(123, @"C:\Apps\test.exe", "\"C:\\Apps\\test.exe\"")]);
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            await expander.ToggleAsync(TestSid);
            await expander.ToggleAsync(TestSid);

            Assert.Single(grid.Rows);
            processList.Verify(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Fact]
    public void ToggleAsync_ExpansionFailureRollsBackExpandedState()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var grid = CreateGrid();
            AddParentRow(grid);
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("scan failed"));
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            await Assert.ThrowsAsync<InvalidOperationException>(() => expander.ToggleAsync(TestSid));

            Assert.False(expander.IsExpanded(TestSid));
            Assert.False(expander.HasExpandedRows);
            Assert.Single(grid.Rows);
        });
    }

    [Fact]
    public void ToggleAsync_ExpansionFailureAfterParentRemovalDoesNotMaskOriginalFailure()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parentRow = AddParentRow(grid);
            var fetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    fetchStarted.TrySetResult(true);
                    releaseFetch.Task.Wait();
                    throw new InvalidOperationException("scan failed");
                });
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var toggleTask = expander.ToggleAsync(TestSid);
            StaTestHelper.PumpUntil(() => fetchStarted.Task.IsCompleted);
            grid.Rows.Remove(parentRow);
            releaseFetch.SetResult(true);
            StaTestHelper.PumpUntil(() => toggleTask.IsCompleted);

            var exception = Assert.IsType<InvalidOperationException>(toggleTask.Exception?.GetBaseException());
            Assert.Equal("scan failed", exception.Message);
            Assert.False(expander.IsExpanded(TestSid));
        });
    }

    [Fact]
    public void ToggleAsync_EmptyExpansionKeepsExpandedState()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            using var grid = CreateGrid();
            AddParentRow(grid);
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns([]);
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            await expander.ToggleAsync(TestSid);

            Assert.True(expander.IsExpanded(TestSid));
            Assert.True(expander.HasExpandedRows);
            Assert.Single(grid.Rows);
        });
    }

    [Fact]
    public void ToggleAsync_StaleExpansionCannotOverwriteNewerExpansion()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            AddParentRow(grid);
            var fetchStarted = new[]
            {
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var releaseFetch = new[]
            {
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var fetchCall = 0;
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    var callIndex = Interlocked.Increment(ref fetchCall) - 1;
                    fetchStarted[callIndex].TrySetResult(true);
                    releaseFetch[callIndex].Task.Wait();
                    var pid = callIndex == 0 ? 111 : 222;
                    return [new ProcessInfo(pid, $@"C:\Apps\{pid}.exe", $@"""C:\Apps\{pid}.exe""")];
                });
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var staleExpansion = expander.ToggleAsync(TestSid);
            StaTestHelper.PumpUntil(() => fetchStarted[0].Task.IsCompleted);
            StaTestHelper.RunAsyncWithMessagePump(() => expander.ToggleAsync(TestSid));
            var currentExpansion = expander.ToggleAsync(TestSid);
            StaTestHelper.PumpUntil(() => fetchStarted[1].Task.IsCompleted);

            releaseFetch[1].SetResult(true);
            StaTestHelper.PumpUntil(() => currentExpansion.IsCompleted);
            releaseFetch[0].SetResult(true);
            StaTestHelper.PumpUntil(() => staleExpansion.IsCompleted);

            var processRow = Assert.IsType<ProcessRow>(grid.Rows[1].Tag);
            Assert.Equal(222, processRow.Process.Pid);
        });
    }

    [Fact]
    public void ToggleAsync_ExpandingDifferentSidDoesNotCancelPendingExpansion()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            AddParentRow(grid);
            AddParentRow(grid, OtherSid);
            var firstFetchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processList = new Mock<IProcessListService>();
            processList
                .Setup(service => service.GetProcessesForSid(TestSid, It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) =>
                {
                    firstFetchStarted.TrySetResult(true);
                    releaseFirstFetch.Task.Wait();
                    return [new ProcessInfo(111, @"C:\Apps\111.exe", @"""C:\Apps\111.exe""")];
                });
            processList
                .Setup(service => service.GetProcessesForSid(OtherSid, It.IsAny<CancellationToken>()))
                .Returns([new ProcessInfo(222, @"C:\Apps\222.exe", @"""C:\Apps\222.exe""")]);
            var expander = new AccountGridProcessExpander(
                processList.Object,
                new ProcessRowGridUpdater(new ProcessCommandLineFormatter()));
            expander.Initialize(grid);

            var firstExpansion = expander.ToggleAsync(TestSid);
            StaTestHelper.PumpUntil(() => firstFetchStarted.Task.IsCompleted);
            StaTestHelper.RunAsyncWithMessagePump(() => expander.ToggleAsync(OtherSid));
            releaseFirstFetch.SetResult(true);
            StaTestHelper.PumpUntil(() => firstExpansion.IsCompleted);

            Assert.Contains(grid.Rows.Cast<DataGridViewRow>(), row =>
                row.Tag is ProcessRow processRow && processRow.Process.Pid == 111);
            Assert.Contains(grid.Rows.Cast<DataGridViewRow>(), row =>
                row.Tag is ProcessRow processRow && processRow.Process.Pid == 222);
        });
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AllowUserToAddRows = false
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Import" });
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Credential" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Logon" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colAllowInternet" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Apps" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProfilePath" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SID" });
        return grid;
    }

    private static DataGridViewRow AddParentRow(DataGridView grid, string sid = TestSid)
    {
        var idx = grid.Rows.Add(false, AccountGridHelper.EmptyIcon, "User", false, false, "", "", sid);
        var row = grid.Rows[idx];
        row.Tag = new AccountRow(null, "User", sid, hasStoredPassword: false);
        return row;
    }
}

using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class ProcessRowGridUpdaterTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void InsertProcessRows_AddsSortedConfiguredRows()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            var updater = CreateUpdater();

            updater.InsertProcessRows(grid, parent, [
                new ProcessInfo(20, @"C:\z.exe", "\"C:\\z.exe\" --z"),
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\" --a")
            ], Sid);

            Assert.Equal(3, grid.Rows.Count);
            var first = Assert.IsType<ProcessRow>(grid.Rows[1].Tag);
            var second = Assert.IsType<ProcessRow>(grid.Rows[2].Tag);
            Assert.Equal(10, first.Process.Pid);
            Assert.Equal("10 a.exe --a", first.DisplayLine);
            Assert.False(first.IsLast);
            Assert.Equal(20, second.Process.Pid);
            Assert.True(second.IsLast);
            Assert.True(grid.Rows[1].ReadOnly);
            Assert.Equal("", grid.Rows[1].Cells["Account"].Value);
            Assert.Equal("", grid.Rows[1].Cells["SID"].Value);
        });
    }

    [Fact]
    public void ApplyFreshProcesses_UpdatesRowsInPlaceWhenPidsMatch()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            var updater = CreateUpdater();
            updater.InsertProcessRows(grid, parent, [
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\" old"),
                new ProcessInfo(20, @"C:\b.exe", "\"C:\\b.exe\" old")
            ], Sid);

            var firstRow = grid.Rows[1];
            updater.ApplyFreshProcesses(grid, parent, [
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\" new"),
                new ProcessInfo(20, @"C:\b.exe", "\"C:\\b.exe\" newer")
            ], Sid);

            Assert.Equal(3, grid.Rows.Count);
            Assert.Same(firstRow, grid.Rows[1]);
            var first = Assert.IsType<ProcessRow>(grid.Rows[1].Tag);
            var second = Assert.IsType<ProcessRow>(grid.Rows[2].Tag);
            Assert.Equal("10 a.exe new", first.DisplayLine);
            Assert.False(first.IsLast);
            Assert.Equal("20 b.exe newer", second.DisplayLine);
            Assert.True(second.IsLast);
        });
    }

    [Fact]
    public void ApplyFreshProcesses_RebuildsRowsWhenPidSetChanges()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            var updater = CreateUpdater();
            updater.InsertProcessRows(grid, parent, [
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\"")
            ], Sid);
            var originalRow = grid.Rows[1];

            updater.ApplyFreshProcesses(grid, parent, [
                new ProcessInfo(20, @"C:\b.exe", "\"C:\\b.exe\" --new")
            ], Sid);

            Assert.Equal(2, grid.Rows.Count);
            Assert.NotSame(originalRow, grid.Rows[1]);
            var row = Assert.IsType<ProcessRow>(grid.Rows[1].Tag);
            Assert.Equal(20, row.Process.Pid);
            Assert.Equal("20 b.exe --new", row.DisplayLine);
        });
    }

    [Fact]
    public void RemoveProcessRowsBelow_RemovesOnlyContiguousProcessRows()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            var updater = CreateUpdater();
            updater.InsertProcessRows(grid, parent, [
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\""),
                new ProcessInfo(20, @"C:\b.exe", "\"C:\\b.exe\"")
            ], Sid);
            AddParentRow(grid, "S-1-5-21-1-2-3-1002");

            updater.RemoveProcessRowsBelow(grid, parent);

            Assert.Equal(2, grid.Rows.Count);
            Assert.IsType<AccountRow>(grid.Rows[0].Tag);
            Assert.IsType<AccountRow>(grid.Rows[1].Tag);
        });
    }

    [Fact]
    public void RestoreSelection_SelectsRowByTagReference()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            AddParentRow(grid, "S-1-5-21-1-2-3-1002");
            var updater = CreateUpdater();

            grid.Rows[1].Selected = true;
            updater.RestoreSelection(grid, parent.Tag!);

            Assert.True(parent.Selected);
            Assert.False(grid.Rows[1].Selected);
        });
    }

    [Fact]
    public void RestoreProcessSelection_SelectsMatchingPidAndOwner()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            var parent = AddParentRow(grid);
            var updater = CreateUpdater();
            updater.InsertProcessRows(grid, parent, [
                new ProcessInfo(10, @"C:\a.exe", "\"C:\\a.exe\""),
                new ProcessInfo(20, @"C:\b.exe", "\"C:\\b.exe\"")
            ], Sid);
            var selected = new ProcessRow(new ProcessInfo(20, null, null), Sid, true, "", 2);

            updater.RestoreProcessSelection(grid, selected);

            Assert.False(grid.Rows[1].Selected);
            Assert.True(grid.Rows[2].Selected);
        });
    }

    [Fact]
    public void RestoreScrollPosition_HandlesValidAndInvalidRowIndexesWithoutVisibleGrid()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var grid = CreateGrid();
            for (int i = 0; i < 40; i++)
                AddParentRow(grid, $"S-1-5-21-1-2-3-{1000 + i}");

            var updater = CreateUpdater();

            updater.RestoreScrollPosition(grid, 10);
            updater.RestoreScrollPosition(grid, -1);
            updater.RestoreScrollPosition(grid, grid.Rows.Count);

            Assert.Equal(40, grid.Rows.Count);
        });
    }

    private static ProcessRowGridUpdater CreateUpdater()
        => new(new ProcessCommandLineFormatter());

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

    private static DataGridViewRow AddParentRow(DataGridView grid, string sid = Sid)
    {
        var idx = grid.Rows.Add(false, AccountGridHelper.EmptyIcon, "Account", false, false, "", "", sid);
        var row = grid.Rows[idx];
        row.Tag = new AccountRow(null, "Account", sid, hasStoredPassword: false);
        return row;
    }
}

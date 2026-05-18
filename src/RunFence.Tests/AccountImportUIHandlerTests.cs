using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountImportUIHandlerTests
{
    [Fact]
    public async Task HandleImportClickAsync_UsesAdapterShowDialogOnly()
    {
        var importHandler = new Mock<IAccountImportHandler>();
        var dialogAdapter = new RecordingOpenFileDialogAdapter();
        var dialogFactory = new Mock<IOpenFileDialogAdapterFactory>();
        dialogFactory.Setup(f => f.Create()).Returns(dialogAdapter);
        var handler = new AccountImportUIHandler(importHandler.Object, dialogFactory.Object);

        var grid = new DataGridView();
        grid.Columns.Add("Import", "Import");
        var row = new DataGridViewRow();
        row.CreateCells(grid);
        row.Cells[0].Value = true;
        row.Tag = new AccountRow(new CredentialEntry { Sid = "S-1-5-21-1000" }, "user", "S-1-5-21-1000", true);
        grid.Rows.Add(row);
        grid.Rows[0].Selected = true;
        var button = new ToolStripButton();
        handler.Initialize(grid, button);

        var context = new FakeAccountsPanelContext();
        await handler.HandleImportClickAsync(context);
        await handler.HandleImportClickAsync(context);

        Assert.Equal(["show"], dialogAdapter.Calls);
    }

    private sealed class RecordingOpenFileDialogAdapter : IOpenFileDialogAdapter
    {
        public OpenFileDialog Dialog { get; } = new();
        public List<string> Calls { get; } = [];
        public DialogResult ShowDialog(IWin32Window? owner)
        {
            Calls.Add("show");
            return DialogResult.Cancel;
        }
        public void Dispose() => Dialog.Dispose();
    }

    private sealed class FakeAccountsPanelContext : IAccountsPanelContext
    {
        public AppDatabase Database { get; } = new();
        public CredentialStore CredentialStore { get; } = new();
        public bool IsRefreshing => false;
        public Control OwnerControl { get; } = new Panel();
        public OperationGuard OperationGuard { get; } = new();
        public bool RenameInProgress { set { } }
        public DialogResult ShowModal(Form dialog) => DialogResult.Cancel;
        public void SaveAndRefresh(Guid? selectCredentialId = null, int fallbackIndex = -1) { }
        public void RefreshAndNotifyDataChanged(Guid? selectCredentialId = null, int fallbackIndex = -1) { }
        public void UpdateStatus(string text) { }
        public void UpdateButtonState() { }
        public void SetControlsEnabled(bool enabled) { }
        public void SaveLastPrefsPath(string path) { }
        public void RefreshGrid() { }
        public long BeginProcessRefreshGeneration() => 1;
        public void TriggerProcessRefresh(long generation, int delayMs = 0) { }
    }
}

using RunFence.Firewall.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistPortsControllerTests
{
    [Fact]
    public void ConfigureContextMenu_UsesStableNames_WhenDisplayTextChanges()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var fixture = CreateFixture();
            fixture.View.PortsAddItem.Text = "Insert port";
            fixture.View.PortsRemoveItem.Text = "Delete port";
            fixture.View.PortsExportItem.Text = "Save ports";

            fixture.Controller.ConfigureContextMenu();

            Assert.True(fixture.View.PortsAddItem.Available);
            Assert.False(fixture.View.PortsRemoveItem.Available);
            Assert.False(fixture.View.PortsExportItem.Available);
        });
    }

    private static PortsFixture CreateFixture()
    {
        var view = new FakeFirewallAllowlistDialogView();
        var handler = new FirewallPortsTabHandler(new FirewallPortValidator(), null);
        var gridHelper = new FirewallPortsGridHelper(
            view.PortsGrid,
            handler,
            (_, _) => true,
            () => { },
            view.UpdateApplyButton,
            view.ShowInformation,
            view.ShowWarning);

        return new PortsFixture(view, new FirewallAllowlistPortsController(view, handler, gridHelper));
    }

    private sealed record PortsFixture(
        FakeFirewallAllowlistDialogView View,
        FirewallAllowlistPortsController Controller);

    private sealed class FakeFirewallAllowlistDialogView : IFirewallAllowlistDialogView
    {
        public DataGridView AllowlistGrid { get; } = new();
        public DataGridView PortsGrid { get; } = CreatePortsGrid();
        public ToolStripMenuItem PortsAddItem => (ToolStripMenuItem)PortsGrid.ContextMenuStrip!.Items[0];
        public ToolStripMenuItem PortsRemoveItem => (ToolStripMenuItem)PortsGrid.ContextMenuStrip!.Items[1];
        public ToolStripMenuItem PortsExportItem => (ToolStripMenuItem)PortsGrid.ContextMenuStrip!.Items[2];
        public bool IsInternetTabSelected => false;
        public bool IsResolvingDomains => false;
        public int SelectedAllowlistRowCount => 0;
        public int SelectedPortRowCount => PortsGrid.SelectedRows.Count;
        public bool AllowInternetChecked => true;
        public bool AllowLanChecked => true;
        public bool AllowLocalhostChecked => true;
        public bool FilterEphemeralChecked => true;
        public IntPtr Handle => IntPtr.Zero;

        public string? PromptInput(string title, string prompt) => null;
        public void SetDnsLabelText(string text) { }
        public void SetFilterEphemeralEnabled(bool enabled) { }
        public void SetWarningVisibility(bool internetWarningVisible, bool portsWarningVisible) { }
        public void SetToolbarState(bool addEnabled, string addToolTipText, bool removeEnabled, string removeToolTipText, string exportToolTipText, bool resolveEnabled, bool viewBlockedEnabled) { }
        public void SetInteractionEnabled(bool enabled, bool filterEphemeralEnabled) { }
        public void SetApplyButtonEnabled(bool enabled) { }
        public void CommitGridEdits() { }
        public void RaiseApplied(FirewallApplyEventArgs args) { }
        public void SetAppliedValues(List<RunFence.Core.Models.FirewallAllowlistEntry> result, bool allowInternet, bool allowLan, bool allowLocalhost, IReadOnlyList<string> allowedLocalhostPorts, bool filterEphemeralLoopback) { }
        public DialogResult ShowDiscardChangesPrompt() => DialogResult.Yes;
        public void ShowInformation(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
        public void RequestClose() { }
        public void UpdateApplyButton() { }
        public void RefreshToolbarState() { }

        private static DataGridView CreatePortsGrid()
        {
            var grid = new DataGridView { AllowUserToAddRows = false };
            grid.Columns.Add("Port", "Port");

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(new ToolStripMenuItem("Add...") { Name = FirewallAllowlistContextMenuItemNames.PortsAdd });
            contextMenu.Items.Add(new ToolStripMenuItem("Remove") { Name = FirewallAllowlistContextMenuItemNames.PortsRemove, Visible = false });
            contextMenu.Items.Add(new ToolStripMenuItem("Export Selected") { Name = FirewallAllowlistContextMenuItemNames.PortsExport, Visible = false });
            grid.ContextMenuStrip = contextMenu;
            return grid;
        }
    }
}

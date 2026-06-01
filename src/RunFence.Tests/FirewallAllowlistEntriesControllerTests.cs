using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Licensing;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistEntriesControllerTests
{
    [Fact]
    public void HandleResolveDomainsAsync_WithoutDomainEntries_ShowsInformation()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var fixture = CreateFixture();

            await fixture.Controller.HandleResolveDomainsAsync(CancellationToken.None);

            Assert.Equal("Resolve", fixture.View.LastInfoTitle);
            Assert.Equal("No domain entries to resolve.", fixture.View.LastInfoMessage);
        });
    }

    [Fact]
    public void ConfigureContextMenu_UsesStableNames_WhenDisplayTextChanges()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var fixture = CreateFixture();
            fixture.View.AllowlistAddItem.Text = "Insert";
            fixture.View.AllowlistRemoveItem.Text = "Delete entry";
            fixture.View.AllowlistExportItem.Text = "Send out";
            var rowIndex = fixture.View.AllowlistGrid.Rows.Add("Domain", "example.com", "");
            using var host = CreateHost(fixture.View.AllowlistGrid);

            var y = fixture.View.AllowlistGrid.ColumnHeadersHeight + (fixture.View.AllowlistGrid.Rows[rowIndex].Height / 2);
            fixture.Controller.HandleMouseDown(4, y);
            fixture.Controller.ConfigureContextMenu();

            Assert.False(fixture.View.AllowlistAddItem.Available);
            Assert.True(fixture.View.AllowlistRemoveItem.Available);
            Assert.True(fixture.View.AllowlistExportItem.Available);
        });
    }

    private static EntriesFixture CreateFixture()
    {
        var networkInfo = new Mock<IFirewallNetworkInfo>();
        networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        networkInfo.Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>());

        var dnsResolver = new Mock<IDnsResolver>();
        dnsResolver.Setup(d => d.ResolveAsync(It.IsAny<string>())).ReturnsAsync([]);

        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.CanAddFirewallAllowlistEntry(It.IsAny<int>())).Returns(true);

        var view = new FakeFirewallAllowlistDialogView();
        var handler = new FirewallAllowlistTabHandler(
            new FirewallAllowlistValidator(licenseService.Object),
            new FirewallDomainResolver(networkInfo.Object, dnsResolver.Object),
            []);
        var importExportService = new FirewallAllowlistImportExportService();
        var importExportHelper = new FirewallAllowlistImportExportHelper(
            importExportService,
            handler.GetEntries,
            () => Array.Empty<string>(),
            view);
        var gridHelper = new FirewallAllowlistGridHelper(
            view.AllowlistGrid,
            handler,
            importExportHelper.TryExportToFile,
            importExportHelper.TryExportCombinedToFile,
            view.UpdateApplyButton,
            view.RefreshToolbarState,
            view.ShowInformation,
            view.ShowWarning,
            view.ShowError);

        return new EntriesFixture(
            view,
            new FirewallAllowlistEntriesController(view, handler, gridHelper));
    }

    private static Form CreateHost(Control control)
    {
        var form = new Form { Width = 400, Height = 250 };
        control.Dock = DockStyle.Fill;
        form.Controls.Add(control);
        StaTestHelper.CreateControlTree(form);
        Application.DoEvents();
        return form;
    }

    private sealed record EntriesFixture(
        FakeFirewallAllowlistDialogView View,
        FirewallAllowlistEntriesController Controller);

    private sealed class FakeFirewallAllowlistDialogView : IFirewallAllowlistDialogView
    {
        public DataGridView AllowlistGrid { get; } = CreateAllowlistGrid();
        public DataGridView PortsGrid { get; } = CreatePortsGrid();
        public ToolStripMenuItem AllowlistAddItem => (ToolStripMenuItem)AllowlistGrid.ContextMenuStrip!.Items[0];
        public ToolStripMenuItem AllowlistRemoveItem => (ToolStripMenuItem)AllowlistGrid.ContextMenuStrip!.Items[1];
        public ToolStripMenuItem AllowlistExportItem => (ToolStripMenuItem)AllowlistGrid.ContextMenuStrip!.Items[2];
        public bool IsInternetTabSelected { get; set; } = true;
        public bool IsResolvingDomains { get; set; }
        public int SelectedAllowlistRowCount => AllowlistGrid.SelectedRows.Count;
        public int SelectedPortRowCount => PortsGrid.SelectedRows.Count;
        public bool AllowInternetChecked { get; set; } = true;
        public bool AllowLanChecked { get; set; } = true;
        public bool AllowLocalhostChecked { get; set; } = true;
        public bool FilterEphemeralChecked { get; set; } = true;
        public string? PromptResult { get; set; }
        public string? LastInfoTitle { get; private set; }
        public string? LastInfoMessage { get; private set; }
        public IntPtr Handle => IntPtr.Zero;

        public string? PromptInput(string title, string prompt) => PromptResult;
        public void SetDnsLabelText(string text) { }
        public void SetFilterEphemeralEnabled(bool enabled) { }
        public void SetWarningVisibility(bool internetWarningVisible, bool portsWarningVisible) { }
        public void SetToolbarState(bool addEnabled, string addToolTipText, bool removeEnabled, string removeToolTipText, string exportToolTipText, bool resolveEnabled, bool viewBlockedEnabled) { }
        public void SetInteractionEnabled(bool enabled, bool filterEphemeralEnabled) { }
        public void SetApplyButtonEnabled(bool enabled) { }
        public void CommitGridEdits() { }
        public void RaiseApplied(FirewallApplyEventArgs args) { }
        public void SetAppliedValues(List<FirewallAllowlistEntry> result, bool allowInternet, bool allowLan, bool allowLocalhost, IReadOnlyList<string> allowedLocalhostPorts, bool filterEphemeralLoopback) { }
        public DialogResult ShowDiscardChangesPrompt() => DialogResult.Yes;

        public void ShowInformation(string title, string message)
        {
            LastInfoTitle = title;
            LastInfoMessage = message;
        }

        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
        public void RequestClose() { }
        public void UpdateApplyButton() { }
        public void RefreshToolbarState() { }

        private static DataGridView CreateAllowlistGrid()
        {
            var grid = new DataGridView { AllowUserToAddRows = false };
            grid.Columns.Add("Type", "Type");
            grid.Columns.Add("Value", "Value");
            grid.Columns.Add("Resolved", "Resolved");

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(new ToolStripMenuItem("Add...") { Name = FirewallAllowlistContextMenuItemNames.AllowlistAdd });
            contextMenu.Items.Add(new ToolStripMenuItem("Remove") { Name = FirewallAllowlistContextMenuItemNames.AllowlistRemove, Visible = false });
            contextMenu.Items.Add(new ToolStripMenuItem("Export Selected") { Name = FirewallAllowlistContextMenuItemNames.AllowlistExport, Visible = false });
            grid.ContextMenuStrip = contextMenu;
            return grid;
        }

        private static DataGridView CreatePortsGrid()
        {
            var grid = new DataGridView { AllowUserToAddRows = false };
            grid.Columns.Add("Port", "Port");
            return grid;
        }
    }
}

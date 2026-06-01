using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Licensing;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistImportExportCoordinatorTests
{
    [Fact]
    public void HandleImport_AddsImportedEntriesAndPorts_AndUpdatesApplyButton()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new FakeView();
            var entriesController = CreateEntriesController(view);
            var portsController = CreatePortsController(view);
            var importFlow = new FakeImportExportFlow(
                new FirewallAllowlistImportedLines(
                    ["example.com"],
                    ["localhost:8080"]));
            var controller = new FirewallAllowlistImportExportCoordinator(
                importFlow,
                entriesController,
                portsController,
                view);

            controller.HandleImport();

            Assert.Single(entriesController.GetEntries());
            Assert.Single(view.PortsGrid.Rows);
            Assert.Equal(1, view.UpdateApplyButtonCallCount);
        });
    }

    [Fact]
    public void HandleExport_RoutesToSelectedTabController()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var view = new FakeView();
            var entriesController = CreateEntriesController(view);
            var portsController = CreatePortsController(view);
            var controller = new FirewallAllowlistImportExportCoordinator(
                new FakeImportExportFlow(null),
                entriesController,
                portsController,
                view);

            entriesController.AddImportedEntries([new FirewallAllowlistEntry { Value = "example.com", IsDomain = true }]);
            portsController.AddImportedPorts(["8080"]);
            view.AllowlistGrid.Rows[0].Selected = true;
            controller.HandleExport(internetTabSelected: true);
            Assert.Equal(1, view.ExportAttemptCount);
            Assert.Equal("Export Firewall Allowlist", view.LastExportTitle);

            view.AllowlistGrid.ClearSelection();
            view.PortsGrid.Rows[0].Selected = true;
            controller.HandleExport(internetTabSelected: false);

            Assert.Equal(2, view.ExportAttemptCount);
            Assert.Equal("Export Port Exceptions", view.LastExportTitle);
        });
    }

    private sealed class FakeImportExportFlow(FirewallAllowlistImportedLines? result) : IFirewallAllowlistImportExportFlow
    {
        public FirewallAllowlistImportedLines? Import() => result;
    }

    private static FirewallAllowlistEntriesController CreateEntriesController(FakeView view)
    {
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.CanAddFirewallAllowlistEntry(It.IsAny<int>())).Returns(true);

        var networkInfo = new Mock<IFirewallNetworkInfo>();
        networkInfo.Setup(info => info.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>());

        var dnsResolver = new Mock<IDnsResolver>();
        dnsResolver.Setup(resolver => resolver.ResolveAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<string>());

        var handler = new FirewallAllowlistTabHandler(
            new FirewallAllowlistValidator(licenseService.Object),
            new FirewallDomainResolver(networkInfo.Object, dnsResolver.Object),
            []);
        return new FirewallAllowlistEntriesController(
            view,
            handler,
            new FirewallAllowlistGridHelper(
                view.AllowlistGrid,
                handler,
                view.TryExportToFile,
                view.ExportCombined,
                () => { },
                view.RefreshToolbarState,
                view.ShowInformation,
                view.ShowWarning,
                view.ShowError));
    }

    private static FirewallAllowlistPortsController CreatePortsController(FakeView view)
    {
        var handler = new FirewallPortsTabHandler(new FirewallPortValidator(), null);
        return new FirewallAllowlistPortsController(
            view,
            handler,
            new FirewallPortsGridHelper(
                view.PortsGrid,
                handler,
                view.TryExportToFile,
                view.ExportCombined,
                () => { },
                view.ShowInformation,
                view.ShowWarning));
    }

    private sealed class FakeView : IFirewallAllowlistDialogView
    {
        public DataGridView AllowlistGrid { get; } = CreateAllowlistGrid();
        public DataGridView PortsGrid { get; } = CreatePortsGrid();
        public int ExportAttemptCount { get; private set; }
        public string? LastExportTitle { get; private set; }
        public string? LastInfoTitle { get; private set; }
        public string? LastInfoMessage { get; private set; }
        public int UpdateApplyButtonCallCount { get; private set; }
        public bool IsInternetTabSelected => true;
        public bool IsResolvingDomains => false;
        public int SelectedAllowlistRowCount => AllowlistGrid.SelectedRows.Count;
        public int SelectedPortRowCount => PortsGrid.SelectedRows.Count;
        public bool AllowInternetChecked => true;
        public bool AllowLanChecked => true;
        public bool AllowLocalhostChecked => true;
        public bool FilterEphemeralChecked => true;
        public IntPtr Handle => IntPtr.Zero;

        public bool TryExportToFile(IReadOnlyList<string> entries, string title)
        {
            ExportAttemptCount++;
            LastExportTitle = title;
            return true;
        }

        public void ExportCombined()
        {
            ExportAttemptCount++;
            LastExportTitle = "Export Firewall Settings";
        }

        public string? PromptInput(string title, string prompt) => null;
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
        public void UpdateApplyButton() => UpdateApplyButtonCallCount++;
        public void RefreshToolbarState() { }

        private static DataGridView CreateAllowlistGrid()
        {
            var grid = new DataGridView { AllowUserToAddRows = false };
            grid.Columns.Add("Type", "Type");
            grid.Columns.Add("Value", "Value");
            grid.Columns.Add("Resolved", "Resolved");
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

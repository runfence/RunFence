using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallBlockedConnectionsDialogControllerTests
{
    [Fact]
    public void OpenDialog_PassesExistingEntries_AndAddsSelectedEntries()
    {
        var existingEntry = new FirewallAllowlistEntry { Value = "existing.example", IsDomain = true };
        var selectedEntry = new FirewallAllowlistEntry { Value = "blocked.example", IsDomain = true };
        var view = new FakeView();
        var entriesController = CreateEntriesController(view, [existingEntry], licenseLimitMessage: null);
        var flow = new FakeBlockedConnectionsDialogFlow([selectedEntry]);
        var controller = new FirewallBlockedConnectionsDialogController(view, flow, entriesController);

        controller.OpenDialog(enableAuditLogging: true);

        Assert.Same(view, flow.LastOwner);
        Assert.True(flow.LastEnableAuditLogging);
        Assert.Equal(existingEntry.Value, Assert.Single(flow.LastExistingEntries!).Value);
        Assert.Equal(selectedEntry.Value, entriesController.GetEntries().Last().Value);
    }

    [Fact]
    public void OpenDialog_WhenEntriesAreTruncated_ShowsLicenseLimitMessage()
    {
        var selectedEntry = new FirewallAllowlistEntry { Value = "blocked.example", IsDomain = true };
        var view = new FakeView();
        var entriesController = CreateEntriesController(view, [], "Limit reached.", maxEntries: 0);
        var controller = new FirewallBlockedConnectionsDialogController(
            view,
            new FakeBlockedConnectionsDialogFlow([selectedEntry]),
            entriesController);

        controller.OpenDialog();

        Assert.Equal("License Limit", view.LastInfoTitle);
        Assert.Equal("Limit reached.", view.LastInfoMessage);
    }

    private sealed class FakeBlockedConnectionsDialogFlow(List<FirewallAllowlistEntry>? result) : IBlockedConnectionsDialogFlow
    {
        public IReadOnlyList<FirewallAllowlistEntry>? LastExistingEntries { get; private set; }
        public IWin32Window? LastOwner { get; private set; }
        public bool LastEnableAuditLogging { get; private set; }

        public List<FirewallAllowlistEntry>? ShowDialog(
            IReadOnlyList<FirewallAllowlistEntry> existingEntries,
            IWin32Window owner,
            bool enableAuditLogging = false)
        {
            LastExistingEntries = existingEntries.ToList();
            LastOwner = owner;
            LastEnableAuditLogging = enableAuditLogging;
            return result;
        }
    }

    private static FirewallAllowlistEntriesController CreateEntriesController(
        FakeView view,
        IReadOnlyList<FirewallAllowlistEntry> entries,
        string? licenseLimitMessage,
        int maxEntries = int.MaxValue)
    {
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.CanAddFirewallAllowlistEntry(It.IsAny<int>()))
            .Returns<int>(count => count < maxEntries);
        licenseService.Setup(service => service.GetRestrictionMessage(EvaluationFeature.FirewallAllowlist, It.IsAny<int>()))
            .Returns(licenseLimitMessage);
        var networkInfo = new Mock<IFirewallNetworkInfo>();
        networkInfo.Setup(info => info.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>());
        var dnsResolver = new Mock<IDnsResolver>();
        dnsResolver.Setup(resolver => resolver.ResolveAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<string>());
        var handler = new FirewallAllowlistTabHandler(
            new FirewallAllowlistValidator(licenseService.Object),
            new FirewallDomainResolver(networkInfo.Object, dnsResolver.Object),
            entries.ToList());
        var controller = new FirewallAllowlistEntriesController(
            view,
            handler,
            new FirewallAllowlistGridHelper(
                view.AllowlistGrid,
                handler,
                (_, _) => true,
                () => { },
                view.UpdateApplyButton,
                view.RefreshToolbarState,
                view.ShowInformation,
                view.ShowWarning,
                view.ShowError));
        controller.Initialize();
        return controller;
    }

    private sealed class FakeView : IFirewallAllowlistDialogView
    {
        public bool IsInternetTabSelected => true;
        public bool IsResolvingDomains => false;
        public int SelectedAllowlistRowCount => 0;
        public int SelectedPortRowCount => 0;
        public bool AllowInternetChecked => true;
        public bool AllowLanChecked => true;
        public bool AllowLocalhostChecked => true;
        public bool FilterEphemeralChecked => true;
        public DataGridView AllowlistGrid { get; } = CreateAllowlistGrid();
        public DataGridView PortsGrid { get; } = new();
        public string? LastInfoTitle { get; private set; }
        public string? LastInfoMessage { get; private set; }
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
            return grid;
        }
    }
}

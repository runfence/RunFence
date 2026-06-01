using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Licensing;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistDialogTests
{
    [Fact]
    public void Construction_InitializesRuntimeImagesAndSetsTitle()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var networkInfo = new RecordingFirewallNetworkInfo(["192.0.2.53"]);
            using var dialog = CreateDialog(networkInfo, new RecordingDnsResolver());
            var items = GetToolStripItems(dialog)
                .Where(item => item is not ToolStripSeparator)
                .ToList();

            Assert.Equal("Internet Allowlist — Sandbox User", dialog.Text);
            AssertItemHasImage(items, item => item is ToolStripButton && item.ToolTipText is string toolTip && toolTip.StartsWith("Add entry (IP, CIDR, or domain", StringComparison.Ordinal), "allowlist add toolbar button");
            AssertItemHasImage(items, item => item is ToolStripButton && item.ToolTipText == "Remove", "remove toolbar button");
            AssertItemHasImage(items, item => item is ToolStripButton && item.ToolTipText is string toolTip && toolTip.StartsWith("Export selected entries to file", StringComparison.Ordinal), "export toolbar button");
            AssertItemHasImage(items, item => item is ToolStripButton && item.ToolTipText == "Import entries and ports from file", "import toolbar button");
            AssertItemHasImage(items, item => item is ToolStripButton && item.ToolTipText == "Resolve DNS", "resolve toolbar button");
            AssertItemHasImage(items, item => item is ToolStripButton && item.Text == "View Blocked", "view blocked toolbar button");
            AssertItemHasImage(items, item => item.Name == FirewallAllowlistContextMenuItemNames.AllowlistRemove, "allowlist remove context item");
            AssertItemHasImage(items, item => item.Name == FirewallAllowlistContextMenuItemNames.AllowlistExport, "allowlist export context item");
            AssertItemHasImage(items, item => item.Name == FirewallAllowlistContextMenuItemNames.PortsRemove, "ports remove context item");
            AssertItemHasImage(items, item => item.Name == FirewallAllowlistContextMenuItemNames.PortsExport, "ports export context item");
        });
    }

    [Fact]
    public void OnLoad_Twice_DoesNotDuplicateRowsOrSideEffects()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var networkInfo = new RecordingFirewallNetworkInfo(["192.0.2.53", "192.0.2.54"]);
            var dnsResolver = new RecordingDnsResolver();
            using var dialog = CreateDialog(networkInfo, dnsResolver);
            var view = (IFirewallAllowlistDialogView)dialog;

            StaTestHelper.CreateControlTree(dialog);
            dialog.InvokeOnLoad();

            var firstAllowlistRowCount = CountDataRows(view.AllowlistGrid);
            var firstPortsRowCount = CountDataRows(view.PortsGrid);
            var dnsLabel = FindLabel(dialog, text => text.StartsWith("DNS servers", StringComparison.Ordinal));
            FindLabel(dialog, text => text == "Allowlist entries only apply when Internet or LAN access is blocked");
            FindLabel(dialog, text => text == "Port exceptions only apply when Localhost access is blocked");
            var applyButton = FindButton(dialog, "Apply");
            var firstDnsText = dnsLabel.Text;
            var firstGetDnsCalls = networkInfo.GetDnsServerAddressesCallCount;

            dialog.InvokeOnLoad();

            Assert.Equal(firstAllowlistRowCount, CountDataRows(view.AllowlistGrid));
            Assert.Equal(firstPortsRowCount, CountDataRows(view.PortsGrid));
            Assert.Equal(firstDnsText, dnsLabel.Text);
            Assert.False(applyButton.Enabled);
            Assert.Equal(1, firstGetDnsCalls);
            Assert.Equal(firstGetDnsCalls, networkInfo.GetDnsServerAddressesCallCount);
            Assert.Equal(0, networkInfo.ResolveDomainEntriesAsyncCallCount);
            Assert.Equal(0, dnsResolver.ResolveAsyncCallCount);
        });
    }

    [Fact]
    public void SetAppliedValues_UpdatesPublicResultProperties()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog(new RecordingFirewallNetworkInfo(["192.0.2.53"]), new RecordingDnsResolver());
            var view = (IFirewallAllowlistDialogView)dialog;
            var result = new List<FirewallAllowlistEntry>
            {
                new() { Value = "203.0.113.20", IsDomain = false }
            };
            IReadOnlyList<string> ports = ["53", "443-445"];

            view.SetAppliedValues(result, allowInternet: false, allowLan: true, allowLocalhost: false, ports, filterEphemeralLoopback: false);

            Assert.Same(result, dialog.Result);
            Assert.False(dialog.AllowInternet);
            Assert.True(dialog.AllowLan);
            Assert.False(dialog.AllowLocalhost);
            Assert.Equal(ports, dialog.AllowedLocalhostPorts);
            Assert.False(dialog.FilterEphemeralLoopback);
        });
    }

    private static TestableFirewallAllowlistDialog CreateDialog(
        RecordingFirewallNetworkInfo networkInfo,
        RecordingDnsResolver dnsResolver)
    {
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.CanAddFirewallAllowlistEntry(It.IsAny<int>())).Returns(true);
        var componentFactory = new FirewallAllowlistDialogComponentFactory(
            networkInfo,
            new FirewallAllowlistValidator(licenseService.Object),
            new FirewallPortValidator(),
            new FirewallDomainResolver(networkInfo, dnsResolver),
            new BlockedConnectionsFlowHelper(
                new BlockedConnectionAggregator(),
                Mock.Of<IBlockedConnectionReader>(),
                Mock.Of<IAuditPolicyService>(),
                new FirewallEnforcementRetryState(),
                Mock.Of<IFirewallDomainRefreshRequester>(),
                dnsResolver),
            new FirewallAllowlistImportExportService(),
            new FirewallDialogApplyPresenter());

        return new TestableFirewallAllowlistDialog(
            new FirewallAllowlistInitialState(
                Current:
                [
                    new FirewallAllowlistEntry { Value = "192.0.2.10", IsDomain = false },
                    new FirewallAllowlistEntry { Value = "198.51.100.15", IsDomain = false }
                ],
                DisplayName: "Sandbox User",
                AllowInternet: true,
                AllowLan: true,
                AllowLocalhost: true,
                AllowedLocalhostPorts: ["53", "80-81"],
                FilterEphemeralLoopback: true),
            componentFactory);
    }

    private static int CountDataRows(DataGridView grid) =>
        grid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow);

    private static IEnumerable<ToolStripItem> GetToolStripItems(Control root)
    {
        var seenItems = new HashSet<ToolStripItem>();
        foreach (var item in GetToolStripItemsCore(root, seenItems))
            yield return item;
    }

    private static IEnumerable<ToolStripItem> GetToolStripItemsCore(Control control, HashSet<ToolStripItem> seenItems)
    {
        if (control.ContextMenuStrip != null)
        {
            foreach (var item in GetToolStripItems(control.ContextMenuStrip.Items, seenItems))
                yield return item;
        }

        if (control is ToolStrip toolStrip)
        {
            foreach (var item in GetToolStripItems(toolStrip.Items, seenItems))
                yield return item;
        }

        foreach (Control child in control.Controls)
        {
            foreach (var item in GetToolStripItemsCore(child, seenItems))
                yield return item;
        }
    }

    private static IEnumerable<ToolStripItem> GetToolStripItems(ToolStripItemCollection items, HashSet<ToolStripItem> seenItems)
    {
        foreach (ToolStripItem item in items)
        {
            if (seenItems.Add(item))
                yield return item;

            if (item is ToolStripDropDownItem dropDownItem)
            {
                foreach (var childItem in GetToolStripItems(dropDownItem.DropDownItems, seenItems))
                    yield return childItem;
            }
        }
    }

    private static void AssertItemHasImage(
        IReadOnlyList<ToolStripItem> items,
        Func<ToolStripItem, bool> predicate,
        string description)
    {
        var item = items.Single(predicate);
        Assert.True(item.Image != null, $"Expected image for {description}.");
    }

    private static Label FindLabel(Control root, Func<string, bool> predicate) =>
        root.Controls
            .Cast<Control>()
            .SelectMany(GetDescendantsAndSelf)
            .OfType<Label>()
            .Single(label => predicate(label.Text));

    private static Button FindButton(Control root, string text) =>
        root.Controls
            .Cast<Control>()
            .SelectMany(GetDescendantsAndSelf)
            .OfType<Button>()
            .Single(button => button.Text == text);

    private static IEnumerable<Control> GetDescendantsAndSelf(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
        {
            foreach (var descendant in GetDescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private sealed class TestableFirewallAllowlistDialog(
        FirewallAllowlistInitialState initialState,
        FirewallAllowlistDialogComponentFactory componentFactory) : FirewallAllowlistDialog(initialState, componentFactory)
    {
        public void InvokeOnLoad() => OnLoad(EventArgs.Empty);
    }

    private sealed class RecordingFirewallNetworkInfo(IReadOnlyList<string> dnsServers) : IFirewallNetworkInfo
    {
        public int GetDnsServerAddressesCallCount { get; private set; }
        public int ResolveDomainEntriesAsyncCallCount { get; private set; }

        public IReadOnlyList<string> GetDnsServerAddresses()
        {
            GetDnsServerAddressesCallCount++;
            return dnsServers;
        }

        public Task<Dictionary<string, List<string>>> ResolveDomainEntriesAsync(IReadOnlyList<FirewallAllowlistEntry> entries)
        {
            ResolveDomainEntriesAsyncCallCount++;
            return Task.FromResult(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class RecordingDnsResolver : IDnsResolver
    {
        public int ResolveAsyncCallCount { get; private set; }

        public Task<IReadOnlyList<string>> ResolveAsync(string hostname)
        {
            ResolveAsyncCallCount++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<IReadOnlyList<string>> ResolveReverseAsync(string ipAddress) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

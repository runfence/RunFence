using System.Text.RegularExpressions;
using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Licensing;
using RunFence.Tests.Helpers;
using RunFence.UI.Controls;
using Xunit;

namespace RunFence.Tests;

public sealed class FirewallAllowlistDialogDesignerTests
{
    [Fact]
    public void DesignerSource_DoesNotContainRuntimeIconFactoryCalls()
    {
        var source = ReadRunFenceSource("Firewall", "UI", "Forms", "FirewallAllowlistDialog.Designer.cs");

        Assert.DoesNotContain("UiIconFactory.CreateToolbarIcon", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConstructor_AssignsToolbarAndContextMenuImagesAfterInitializeComponent()
    {
        var source = ReadRunFenceSource("Firewall", "UI", "Forms", "FirewallAllowlistDialog.cs");

        Assert.Matches(
            new Regex(@"InitializeComponent\(\);\s*InitializeRuntimeImages\(\);", RegexOptions.Singleline),
            source);
        Assert.Contains("private void InitializeRuntimeImages()", source, StringComparison.Ordinal);

        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            StaTestHelper.CreateControlTree(dialog);
            Application.DoEvents();

            var toolStrip = FindControls<ToolStrip>(dialog).Single();
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[0]).Image);
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[1]).Image);
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[2]).Image);
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[3]).Image);
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[5]).Image);
            Assert.NotNull(Assert.IsType<ToolStripButton>(toolStrip.Items[7]).Image);

            var grids = FindControls<StyledDataGridView>(dialog).ToList();
            var allowlistContextMenu = grids.Single(grid => grid.Columns.Count == 3).ContextMenuStrip;
            var portsContextMenu = grids.Single(grid => grid.Columns.Count == 1).ContextMenuStrip;
            Assert.NotNull(allowlistContextMenu);
            Assert.NotNull(portsContextMenu);

            Assert.NotNull(FindMenuItem(allowlistContextMenu!, "Remove").Image);
            Assert.NotNull(FindMenuItem(allowlistContextMenu, "Export Selected").Image);
            Assert.NotNull(FindMenuItem(portsContextMenu!, "Remove").Image);
            Assert.NotNull(FindMenuItem(portsContextMenu, "Export Selected").Image);
        });
    }

    private static FirewallAllowlistDialog CreateDialog()
    {
        var networkInfo = new Mock<IFirewallNetworkInfo>();
        networkInfo.Setup(n => n.GetDnsServerAddresses()).Returns(["192.0.2.53"]);
        networkInfo.Setup(n => n.ResolveDomainEntriesAsync(It.IsAny<IReadOnlyList<FirewallAllowlistEntry>>()))
            .ReturnsAsync(new Dictionary<string, List<string>>());

        var dnsResolver = new Mock<IDnsResolver>();
        dnsResolver.Setup(d => d.ResolveAsync(It.IsAny<string>())).ReturnsAsync([]);

        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.CanAddFirewallAllowlistEntry(It.IsAny<int>())).Returns(true);

        var retryState = new FirewallEnforcementRetryState();
        var refreshRequester = new Mock<IFirewallDomainRefreshRequester>();

        return new FirewallAllowlistDialog(
            [],
            networkInfo.Object,
            new FirewallAllowlistValidator(licenseService.Object),
            new FirewallPortValidator(),
            new FirewallDomainResolver(networkInfo.Object, dnsResolver.Object),
            new BlockedConnectionsFlowHelper(
                new BlockedConnectionAggregator(),
                new Mock<IBlockedConnectionReader>().Object,
                new Mock<IAuditPolicyService>().Object,
                retryState,
                refreshRequester.Object,
                dnsResolver.Object),
            new FirewallAllowlistImportExportService(),
            new FirewallDialogApplyPresenter());
    }

    private static ToolStripMenuItem FindMenuItem(ContextMenuStrip menu, string text) =>
        menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == text);

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static string ReadRunFenceSource(params string[] relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", "RunFence", .. relativePath]));
        return File.ReadAllText(fullPath);
    }
}

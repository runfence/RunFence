using Moq;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Licensing;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class FirewallAllowlistDialogTests
{
    [Fact]
    public void CloseBeforeApply_BehavesNormally()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            StaTestHelper.CreateControlTree(dialog);
            Application.DoEvents();

            var closeButton = FindButton(dialog, "Close");
            Assert.True(closeButton.Enabled);

            dialog.Close();
            Application.DoEvents();

            Assert.True(dialog.IsDisposed);
        });
    }

    [Fact]
    public void ChangingSetting_EnablesApplyButton()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            StaTestHelper.CreateControlTree(dialog);
            Application.DoEvents();

            FindCheckBox(dialog, "LAN").Checked = false;
            var closeButton = FindButton(dialog, "Close");
            var applyButton = FindButton(dialog, "Apply");

            Assert.False(dialog.IsDisposed);
            Assert.True(closeButton.Enabled);
            Assert.True(applyButton.Enabled);
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

    private static Button FindButton(Control root, string text)
        => FindControls<Button>(root).First(button => button.Text == text);

    private static CheckBox FindCheckBox(Control root, string text)
        => FindControls<CheckBox>(root).Single(checkBox => checkBox.Text == text);

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
}

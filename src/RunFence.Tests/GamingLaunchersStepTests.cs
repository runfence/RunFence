using Moq;
using System.Linq;
using System.Windows.Forms;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class GamingLaunchersStepTests
{
    [Fact]
    public void Discover_Selection_AddsDiscoveredLauncherPath()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            var service = new FakeShortcutDiscoveryService([new DiscoveredApp("Launcher", @"C:\Games\Launcher.exe")]);
            var dialogService = new FakeAppDiscoveryDialogService((@"C:\Games\Launcher.exe", "Launcher"));
            using var step = new GamingLaunchersStep(
                _ => { },
                service,
                dialogService,
                Mock.Of<IShortcutIconHelper>(),
                Mock.Of<IOpenFileDialogAdapterFactory>());
            host.Controls.Add(step);

            StaTestHelper.CreateControlTree(host);

            var listBox = FindControls<ListBox>(step).Single();
            var discoverButton = FindControls<ToolStrip>(step)
                .Single()
                .Items
                .OfType<ToolStripButton>()
                .Single(button => button.Text?.StartsWith("Discover", StringComparison.Ordinal) == true);

            discoverButton.PerformClick();
            StaTestHelper.PumpUntil(() =>
                listBox.Items.Cast<string>().Contains(@"C:\Games\Launcher.exe"),
                timeoutMessage: "Timed out waiting for launcher discovery selection.");

            Assert.Single(listBox.Items.Cast<string>());
            Assert.Equal(@"C:\Games\Launcher.exe", listBox.Items.Cast<string>().Single());
        });
    }

    [Fact]
    public void Discover_Cancellation_DoesNotChangeLauncherList()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            var service = new FakeShortcutDiscoveryService([new DiscoveredApp("Launcher", @"C:\Games\Launcher.exe")]);
            var dialogService = new FakeAppDiscoveryDialogService((@"C:\Games\Launcher.exe", "Launcher"));
            using var step = new GamingLaunchersStep(
                _ => { },
                service,
                dialogService,
                Mock.Of<IShortcutIconHelper>(),
                Mock.Of<IOpenFileDialogAdapterFactory>());
            host.Controls.Add(step);
            StaTestHelper.CreateControlTree(host);

            var listBox = FindControls<ListBox>(step).Single();
            var discoverButton = FindControls<ToolStrip>(step)
                .Single()
                .Items
                .OfType<ToolStripButton>()
                .Single(button => button.Text?.StartsWith("Discover", StringComparison.Ordinal) == true);

            discoverButton.PerformClick();
            StaTestHelper.PumpUntil(() =>
                listBox.Items.Cast<string>().Contains(@"C:\Games\Launcher.exe"),
                timeoutMessage: "Timed out waiting for initial launcher discovery selection.");

            dialogService.NextResult = null;
            discoverButton.PerformClick();
            StaTestHelper.PumpUntil(() => !discoverButton.Enabled, timeoutMessage: "Timed out waiting for discover button to disable.");
            StaTestHelper.PumpUntil(() => discoverButton.Enabled, timeoutMessage: "Timed out waiting for discover button to re-enable.");

            var items = listBox.Items.Cast<string>().ToList();
            Assert.Single(items);
            Assert.Equal(@"C:\Games\Launcher.exe", items.Single());
        });
    }

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

    private sealed class FakeShortcutDiscoveryService(IReadOnlyList<DiscoveredApp> discoveredApps) : IShortcutDiscoveryService
    {
        private readonly IReadOnlyList<DiscoveredApp> _discoveredApps = discoveredApps;

        public List<DiscoveredApp> DiscoverApps()
            => _discoveredApps.ToList();

        public ShortcutTraversalCache CreateTraversalCache() => new([]);

        public ShortcutTraversalCache CreateTraversalCache(HashSet<string>? managedSids) => new([]);

        public HashSet<string>? CaptureManagedSids() => [];

        public ShortcutTraversalCache CreateTraversalCacheIfNeeded(IEnumerable<AppEntry> apps) => new([]);
    }

    private sealed class FakeAppDiscoveryDialogService((string path, string? name)? nextResult) : IAppDiscoveryDialogService
    {
        public (string path, string? name)? NextResult { get; set; } = nextResult;

        public (string path, string? name)? ShowDialog(
            IReadOnlyList<DiscoveredApp> apps,
            IShortcutIconHelper iconHelper,
            IWin32Window? owner = null)
            => NextResult;
    }
}

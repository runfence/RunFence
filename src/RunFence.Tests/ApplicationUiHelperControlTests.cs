using Moq;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class ApplicationUiHelperControlTests
{
    [Fact]
    public void HandlerAssociationsSection_RemoveSelection_RoutesRefreshToHost()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            using var section = new HandlerAssociationsSection();
            var host = new Mock<IHandlerAssociationsHost>(MockBehavior.Strict);
            host.Setup(h => h.RefreshMappings());

            form.Controls.Add(section);
            section.Dock = DockStyle.Fill;
            section.Initialize(
                Mock.Of<IHandlerAssociationMutationService>(),
                CreateChildCoordinator(DialogResult.Cancel),
                Mock.Of<IUiIconService>(),
                host.Object);
            section.SetEnabled(true);
            section.SetAssociations([new HandlerAssociationItem(".txt", null)]);
            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            var grid = FindControls<DataGridView>(section).Single();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.Rows[0].Selected = true;

            var removeButton = FindControls<ToolStrip>(section).Single().Items
                .OfType<ToolStripButton>()
                .Single(button => button.ToolTipText == "Remove association");
            removeButton.PerformClick();

            Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
            host.Verify(h => h.RefreshMappings(), Times.Once);
        });
    }

    [Fact]
    public void HandlerAssociationsSection_AddClick_UsesHostModeForSuggestions()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = new HandlerAssociationsSection();
            var mutationService = new Mock<IHandlerAssociationMutationService>(MockBehavior.Strict);
            mutationService
                .Setup(service => service.BuildSuggestions(
                    "C:\\Apps\\Tool.exe",
                    "S-1-5-21-test",
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    HandlerAssociationMode.Direct))
                .Returns([]);
            var host = new Mock<IHandlerAssociationsHost>(MockBehavior.Strict);
            host.Setup(h => h.GetCurrentAssociationMode()).Returns(HandlerAssociationMode.Direct);

            section.ExePath = "C:\\Apps\\Tool.exe";
            section.AccountSid = "S-1-5-21-test";
            section.Initialize(
                mutationService.Object,
                CreateChildCoordinator(DialogResult.Cancel),
                Mock.Of<IUiIconService>(),
                host.Object);
            section.SetEnabled(true);

            var addButton = FindControls<ToolStrip>(section).Single().Items
                .OfType<ToolStripButton>()
                .Single(button => button.ToolTipText == "Add association...");
            addButton.PerformClick();

            mutationService.VerifyAll();
            host.Verify(h => h.GetCurrentAssociationMode(), Times.Once);
        });
    }

    [Fact]
    public void AppPathBrowseControl_BrowseBeforeInitialize_IsNoOp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var control = new AppPathBrowseControl();

            ClickBrowse(control);

            Assert.Equal(string.Empty, control.PathText);
        });
    }

    [Fact]
    public void AppPathBrowseControl_FileMode_UsesOpenFileAdapter()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var control = new AppPathBrowseControl();
            var openFactory = new RecordingOpenFileDialogAdapterFactory("C:\\Apps\\Tool.exe");
            var folderFactory = new RecordingFolderBrowserDialogAdapterFactory("C:\\Folders\\Tool");

            control.Initialize(
                openFactory,
                folderFactory,
                new AppPathBrowseConfiguration(
                    "Select Application",
                    "Executable files (*.exe)|*.exe",
                    null,
                    AppPathBrowseMode.File));

            ClickBrowse(control);

            Assert.Equal("C:\\Apps\\Tool.exe", control.PathText);
            Assert.Equal(1, openFactory.CreateCount);
            Assert.Equal(0, folderFactory.CreateCount);
        });
    }

    [Fact]
    public void AppPathBrowseControl_FolderMode_UsesFolderBrowserAdapter()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var control = new AppPathBrowseControl();
            var openFactory = new RecordingOpenFileDialogAdapterFactory("C:\\Apps\\Tool.exe");
            var folderFactory = new RecordingFolderBrowserDialogAdapterFactory("C:\\Folders\\Tool");

            control.Initialize(
                openFactory,
                folderFactory,
                new AppPathBrowseConfiguration(
                    "Select Folder",
                    "Executable files (*.exe)|*.exe",
                    null,
                    AppPathBrowseMode.Folder));

            ClickBrowse(control);

            Assert.Equal("C:\\Folders\\Tool", control.PathText);
            Assert.Equal(0, openFactory.CreateCount);
            Assert.Equal(1, folderFactory.CreateCount);
            Assert.False(FindControls<Button>(control).Single(button => button.Text == "Discover…").Visible);
        });
    }

    [Fact]
    public void AppPathBrowseControl_DiscoverSelection_UpdatesPathAndDiscoveredName()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var control = CreateInitializedAppPathBrowseControl(
                new FakeShortcutDiscoveryService([new DiscoveredApp("Tool", @"C:\Apps\Tool.exe")]),
                new FakeAppDiscoveryDialogService((@"C:\Apps\Tool.exe", "Tool")));
            host.Controls.Add(control);
            StaTestHelper.CreateControlTree(host);

            ClickDiscover(control);
            WaitForDiscoverCompletion(control);

            Assert.Equal(@"C:\Apps\Tool.exe", control.PathText);
            Assert.Equal("Tool", control.DiscoveredName);
        });
    }

    [Fact]
    public void AppPathBrowseControl_DiscoverCancellation_LeavesPathAndDiscoveredNameUnchanged()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            var dialogService = new FakeAppDiscoveryDialogService((@"C:\Apps\Tool.exe", "Tool"));
            using var control = CreateInitializedAppPathBrowseControl(
                new FakeShortcutDiscoveryService([new DiscoveredApp("Tool", @"C:\Apps\Tool.exe")]),
                dialogService);
            host.Controls.Add(control);
            StaTestHelper.CreateControlTree(host);

            ClickDiscover(control);
            WaitForDiscoverCompletion(control);
            dialogService.NextResult = null;

            ClickDiscover(control);
            WaitForDiscoverCompletion(control);

            Assert.Equal(@"C:\Apps\Tool.exe", control.PathText);
            Assert.Equal("Tool", control.DiscoveredName);
        });
    }

    [Fact]
    public void AppPathBrowseControl_ManualEditAfterDiscover_ClearsDiscoveredName()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var control = CreateInitializedAppPathBrowseControl(
                new FakeShortcutDiscoveryService([new DiscoveredApp("Tool", @"C:\Apps\Tool.exe")]),
                new FakeAppDiscoveryDialogService((@"C:\Apps\Tool.exe", "Tool")));
            host.Controls.Add(control);
            StaTestHelper.CreateControlTree(host);

            ClickDiscover(control);
            WaitForDiscoverCompletion(control);

            control.PathText = @"D:\Manual\Other.exe";
            Application.DoEvents();

            Assert.Equal(@"D:\Manual\Other.exe", control.PathText);
            Assert.Null(control.DiscoveredName);
        });
    }

    [Fact]
    public void AppPathBrowseControl_DiscoverButton_IsReenabledAfterAsyncCompletion()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var discoveryStarted = new ManualResetEventSlim(false);
            using var releaseDiscovery = new ManualResetEventSlim(false);
            using var control = CreateInitializedAppPathBrowseControl(
                new BlockingShortcutDiscoveryService(
                    [new DiscoveredApp("Tool", @"C:\Apps\Tool.exe")],
                    discoveryStarted,
                    releaseDiscovery),
                new FakeAppDiscoveryDialogService((@"C:\Apps\Tool.exe", "Tool")));
            host.Controls.Add(control);
            StaTestHelper.CreateControlTree(host);

            try
            {
                ClickDiscover(control);
                StaTestHelper.PumpUntil(
                    () => discoveryStarted.IsSet && !GetDiscoverButton(control).Enabled,
                    timeoutMessage: "Timed out waiting for Discover to disable during async discovery.");

                releaseDiscovery.Set();
                WaitForDiscoverCompletion(control);

                Assert.True(GetDiscoverButton(control).Enabled);
            }
            finally
            {
                releaseDiscovery.Set();
                StaTestHelper.PumpUntil(
                    () => GetDiscoverButton(control).Enabled,
                    TimeSpan.FromSeconds(2),
                    "Timed out waiting for Discover cleanup to re-enable the button.");
            }
        });
    }

    private static HandlerAssociationsChildDialogCoordinator CreateChildCoordinator(DialogResult dialogResult)
    {
        var modalCoordinator = new Mock<IModalCoordinator>(MockBehavior.Strict);
        modalCoordinator
            .Setup(coordinator => coordinator.ShowModal(It.IsAny<Form>(), It.IsAny<IWin32Window?>()))
            .Returns(dialogResult);
        return new HandlerAssociationsChildDialogCoordinator(
            () => new HandlerAssociationEditDialog(),
            Mock.Of<IExeAssociationRegistryReader>(),
            Mock.Of<IMessageBoxService>(),
            modalCoordinator.Object);
    }

    private static void ClickBrowse(Control root)
        => FindControls<Button>(root).Single(button => button.Text == "Browse…").PerformClick();

    private static void ClickDiscover(Control root)
        => StaTestHelper.ClickButton(GetDiscoverButton(root));

    private static Button GetDiscoverButton(Control root)
        => FindControls<Button>(root).Single(button => button.Text.StartsWith("Discover", StringComparison.Ordinal));

    private static void WaitForDiscoverCompletion(AppPathBrowseControl control)
        => StaTestHelper.PumpUntil(
            () => GetDiscoverButton(control).Enabled,
            timeoutMessage: "Timed out waiting for Discover completion.");

    private static AppPathBrowseControl CreateInitializedAppPathBrowseControl(
        IShortcutDiscoveryService shortcutDiscoveryService,
        IAppDiscoveryDialogService appDiscoveryDialogService)
    {
        var control = new AppPathBrowseControl();
        control.Initialize(
            new RecordingOpenFileDialogAdapterFactory(@"C:\Apps\Ignored.exe"),
            new RecordingFolderBrowserDialogAdapterFactory(@"C:\Folders\Ignored"),
            new AppPathBrowseConfiguration(
                "Select Application",
                "Executable files (*.exe)|*.exe",
                null,
                AppPathBrowseMode.File));
        control.InitializeDiscovery(
            shortcutDiscoveryService,
            Mock.Of<IShortcutIconHelper>(),
            appDiscoveryDialogService);
        return control;
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

    private sealed class RecordingOpenFileDialogAdapterFactory(string selectedPath) : IOpenFileDialogAdapterFactory
    {
        public int CreateCount { get; private set; }

        public IOpenFileDialogAdapter Create()
        {
            CreateCount++;
            return new RecordingOpenFileDialogAdapter(selectedPath);
        }
    }

    private sealed class RecordingOpenFileDialogAdapter(string selectedPath) : IOpenFileDialogAdapter
    {
        public OpenFileDialog Dialog { get; } = new() { FileName = selectedPath };

        public DialogResult ShowDialog(IWin32Window? owner) => DialogResult.OK;

        public void Dispose() => Dialog.Dispose();
    }

    private sealed class RecordingFolderBrowserDialogAdapterFactory(string selectedPath) : IFolderBrowserDialogAdapterFactory
    {
        public int CreateCount { get; private set; }

        public IFolderBrowserDialogAdapter Create()
        {
            CreateCount++;
            return new RecordingFolderBrowserDialogAdapter(selectedPath);
        }
    }

    private sealed class RecordingFolderBrowserDialogAdapter(string selectedPath) : IFolderBrowserDialogAdapter
    {
        public FolderBrowserDialog Dialog { get; } = new() { SelectedPath = selectedPath };

        public DialogResult ShowDialog(IWin32Window? owner) => DialogResult.OK;

        public void Dispose() => Dialog.Dispose();
    }

    private sealed class FakeShortcutDiscoveryService(IReadOnlyList<DiscoveredApp> discoveredApps) : IShortcutDiscoveryService
    {
        public List<DiscoveredApp> DiscoverApps() => discoveredApps.ToList();

        public ShortcutTraversalCache CreateTraversalCache() => new([]);

        public ShortcutTraversalCache CreateTraversalCache(HashSet<string>? managedSids) => new([]);

        public HashSet<string>? CaptureManagedSids() => [];

        public ShortcutTraversalCache CreateTraversalCacheIfNeeded(IEnumerable<AppEntry> apps) => new([]);
    }

    private sealed class BlockingShortcutDiscoveryService(
        IReadOnlyList<DiscoveredApp> discoveredApps,
        ManualResetEventSlim discoveryStarted,
        ManualResetEventSlim releaseDiscovery) : IShortcutDiscoveryService
    {
        public List<DiscoveredApp> DiscoverApps()
        {
            discoveryStarted.Set();
            if (!releaseDiscovery.Wait(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("Timed out waiting for test-controlled shortcut discovery release.");
            return discoveredApps.ToList();
        }

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

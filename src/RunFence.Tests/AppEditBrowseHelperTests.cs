using Moq;
using Xunit;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Resolution;

namespace RunFence.Tests;

public sealed class AppEditBrowseHelperTests
{
    [Fact]
    public void BrowseAndApplyFile_YesReplacesSelectedPathWithSuggestion()
    {
        var messageBox = new FakeMessageBoxService(DialogResult.Yes);
        var suggestionService = CreateSuggestionService(
            "C:\\Apps\\App.exe",
            "C:\\Apps\\App\\bin\\App.Helper.exe",
            HandlerPathIconPresence.HasIcon,
            HandlerPathIconPresence.HasIcon);
        var openFactory = new RecordingOpenFileDialogAdapterFactory("C:\\Apps\\App.exe");
        var helper = CreateHelper(messageBox, suggestionService, openFactory);
        var receiver = new RecordingReceiver();

        helper.BrowseAndApplyFile(receiver);

        Assert.Equal("C:\\Apps\\App\\bin\\App.Helper.exe", receiver.FilePath);
        Assert.NotEmpty(openFactory.Dialog.Dialog.CustomPlaces);
        messageBox.VerifyCount(DialogResult.Yes, 1);
        messageBox.VerifyLastTextContains("Replace with the handler target path?", StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseAndApplyFile_NoLeavesOriginalPath()
    {
        var messageBox = new FakeMessageBoxService(DialogResult.No);
        var suggestionService = CreateSuggestionService(
            "C:\\Apps\\App.exe",
            "C:\\Apps\\App\\bin\\App.Helper.exe",
            HandlerPathIconPresence.HasIcon,
            HandlerPathIconPresence.HasIcon);
        var helper = CreateHelper(messageBox, suggestionService, new RecordingOpenFileDialogAdapterFactory("C:\\Apps\\App.exe"));
        var receiver = new RecordingReceiver();

        helper.BrowseAndApplyFile(receiver);

        Assert.Equal("C:\\Apps\\App.exe", receiver.FilePath);
        messageBox.VerifyCount(DialogResult.No, 1);
    }

    [Fact]
    public async Task DiscoverAndApplyAsync_YesReplacesSelectionWithSuggestion()
    {
        var messageBox = new FakeMessageBoxService(DialogResult.Yes);
        var suggestionService = CreateSuggestionService(
            "C:\\Apps\\Discovered\\Launch.exe",
            "C:\\Apps\\Discovered\\bin\\Launch.Helper.exe",
            HandlerPathIconPresence.HasIcon,
            HandlerPathIconPresence.HasIcon);
        var helper = CreateHelper(
            messageBox,
            suggestionService,
            new RecordingOpenFileDialogAdapterFactory("C:\\Apps\\Discovered\\Launch.exe"),
            discoveryService: new FakeShortcutDiscoveryService([new DiscoveredApp("Launch App", "C:\\Apps\\Discovered\\Launch.exe")]),
            appDiscoveryDialogService: new FakeAppDiscoveryDialogService(("C:\\Apps\\Discovered\\Launch.exe", "Launch App")));
        var receiver = new RecordingReceiver();

        await helper.DiscoverAndApplyAsync(receiver);

        Assert.Equal("C:\\Apps\\Discovered\\bin\\Launch.Helper.exe", receiver.FilePath);
        messageBox.VerifyCount(DialogResult.Yes, 1);
    }

    [Fact]
    public void BrowseAndApplyFile_ForwardsSelectedAccountSidToSuggestionReader()
    {
        const string selectedSid = "S-1-5-21-2000";

        var reader = new TrackingHandlerCommandTargetReader([
                new HandlerCommandTarget(
                    @"C:\Apps\App\App.Helper.exe",
                    HandlerCommandTargetRegistryScope.InteractiveUser,
                    ".exe",
                    "\"C:\\Apps\\Launcher.exe\" \"%1\"",
                    null,
                    null)
        ]);
        var suggestionService = new AppEntryHandlerPathSuggestionService(
            reader,
            new FakeHandlerPathIconProbe(new Dictionary<string, HandlerPathIconPresence>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\Apps\App\App.exe"] = HandlerPathIconPresence.HasIcon,
                [@"C:\Apps\App\App.Helper.exe"] = HandlerPathIconPresence.HasIcon
            }));
        var helper = CreateHelper(
            new FakeMessageBoxService(DialogResult.Yes),
            suggestionService,
            new RecordingOpenFileDialogAdapterFactory(@"C:\Apps\App\App.exe"));
        var receiver = new RecordingReceiver { SelectedAccountSid = selectedSid };

        helper.BrowseAndApplyFile(receiver);

        Assert.Equal(selectedSid, reader.RequestedSid);
    }

    private static AppEditBrowseHelper CreateHelper(
        IMessageBoxService messageBoxService,
        AppEntryHandlerPathSuggestionService suggestionService,
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        IShortcutDiscoveryService? discoveryService = null,
        IAppDiscoveryDialogService? appDiscoveryDialogService = null)
    {
        return new AppEditBrowseHelper(
            discoveryService ?? new FakeShortcutDiscoveryService([]),
            Mock.Of<IShortcutIconHelper>(),
            appDiscoveryDialogService ?? new FakeAppDiscoveryDialogService(null),
            messageBoxService,
            new ShortcutTargetResolver(Mock.Of<IShortcutGateway>()),
            Mock.Of<ISessionProvider>(),
            Mock.Of<IExecutableKindService>(),
            suggestionService,
            openFileDialogFactory,
            Mock.Of<IFolderBrowserDialogAdapterFactory>());
    }

    private static AppEntryHandlerPathSuggestionService CreateSuggestionService(
        string selectedPath,
        string replacementPath,
        HandlerPathIconPresence selectedIconPresence,
        HandlerPathIconPresence replacementIconPresence)
    {
        return new AppEntryHandlerPathSuggestionService(
            new FakeHandlerCommandTargetReader([new HandlerCommandTarget(
                replacementPath,
                HandlerCommandTargetRegistryScope.InteractiveUser,
                ".exe",
                "\"C:\\Apps\\Launcher.exe\" \"%1\"",
                null,
                null)]),
            new FakeHandlerPathIconProbe(new Dictionary<string, HandlerPathIconPresence>(StringComparer.OrdinalIgnoreCase)
            {
                [selectedPath] = selectedIconPresence,
                [replacementPath] = replacementIconPresence
            }));
    }

    private sealed class FakeShortcutDiscoveryService(IReadOnlyList<DiscoveredApp> apps) : IShortcutDiscoveryService
    {
        public List<DiscoveredApp> DiscoverApps() => apps.ToList();

        public ShortcutTraversalCache CreateTraversalCache() => new([]);

        public ShortcutTraversalCache CreateTraversalCache(HashSet<string>? managedSids) => new([]);

        public HashSet<string>? CaptureManagedSids() => [];

        public ShortcutTraversalCache CreateTraversalCacheIfNeeded(IEnumerable<AppEntry> apps) => new([]);
    }

    private sealed class FakeAppDiscoveryDialogService((string path, string? name)? result) : IAppDiscoveryDialogService
    {
        private readonly (string path, string? name)? _result = result;

        public (string path, string? name)? ShowDialog(
            IReadOnlyList<DiscoveredApp> apps,
            IShortcutIconHelper iconHelper,
            IWin32Window? owner = null)
            => _result;
    }

    private sealed class FakeHandlerCommandTargetReader(IReadOnlyList<HandlerCommandTarget> targets) : IHandlerCommandTargetReader
    {
        public IReadOnlyList<HandlerCommandTarget> ReadTargets(string? targetAccountSid) => targets;
    }

    private sealed class TrackingHandlerCommandTargetReader(IReadOnlyList<HandlerCommandTarget> targets) : IHandlerCommandTargetReader
    {
        private readonly IReadOnlyList<HandlerCommandTarget> _targets = targets;

        public string? RequestedSid { get; private set; }

        public IReadOnlyList<HandlerCommandTarget> ReadTargets(string? targetAccountSid)
        {
            RequestedSid = targetAccountSid;
            return _targets;
        }
    }

    private sealed class FakeHandlerPathIconProbe(Dictionary<string, HandlerPathIconPresence> knownPresence)
        : IHandlerPathIconProbe
    {
        public HandlerPathIconPresence GetIconPresence(string path)
            => knownPresence.GetValueOrDefault(path, HandlerPathIconPresence.Unknown);
    }

    private sealed class FakeMessageBoxService(DialogResult defaultResult) : IMessageBoxService
    {
        private readonly List<(string text, DialogResult result)> _calls = [];
        public IReadOnlyList<(string text, DialogResult result)> Calls => _calls;

        public DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            _calls.Add((text, defaultResult));
            return defaultResult;
        }

        public DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Show(text, caption, buttons, icon);

        public void VerifyCount(DialogResult expected, int count)
        {
            Assert.Equal(count, _calls.Count(call => call.result == expected));
        }

        public void VerifyLastTextContains(string expected, StringComparison comparison)
        {
            Assert.Contains(_calls, call =>
                call.text.Contains(expected, comparison));
        }
    }

    private sealed class RecordingOpenFileDialogAdapterFactory(string selectedPath) : IOpenFileDialogAdapterFactory
    {
        public RecordingOpenFileDialogAdapter Dialog { get; private set; } = new(selectedPath);

        public IOpenFileDialogAdapter Create()
            => Dialog = new RecordingOpenFileDialogAdapter(Dialog.Dialog.FileName);
    }

    private sealed class RecordingOpenFileDialogAdapter(string selectedPath) : IOpenFileDialogAdapter
    {
        public OpenFileDialog Dialog { get; } = new OpenFileDialog { FileName = selectedPath };

        public DialogResult ShowDialog(IWin32Window? owner) => DialogResult.OK;

        public void Dispose() => Dialog.Dispose();
    }

    private sealed class RecordingReceiver : IAppEditBrowseResultReceiver
    {
        public string? SelectedAccountSid { get; set; }

        public string? FilePath { get; private set; }
        public string? AppName { get; set; }

        public string GetAppName() => AppName ?? string.Empty;

        public void SetFilePath(string path) => FilePath = path;

        public void SetAppName(string name) => AppName = name;

        public void SetFolderMode(bool isFolder) { }

        public void SetWorkingDir(string path) { }

        public void SetDefaultArgs(string args) { }

        public bool CanSuggestBasicPrivilegeLevel() => false;

        public void SetPrivilegeLevel(PrivilegeLevel? level) { }

        public string? GetSelectedAccountSid() => SelectedAccountSid;
    }
}

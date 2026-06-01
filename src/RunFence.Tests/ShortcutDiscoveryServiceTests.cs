using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class ShortcutDiscoveryServiceTests
{
    [Fact]
    public void CreateTraversalCache_MaterializesTraversalOnce()
    {
        var scanner = new CountingShortcutTraversalScanner(
            [new ShortcutTraversalEntry(@"C:\Links\App.lnk", @"C:\Apps\App.exe", "--flag")]);
        var service = CreateService(scanner);

        var cache = service.CreateTraversalCache();
        var first = cache.Entries;
        var second = cache.Entries;

        Assert.Equal(1, scanner.ScanCount);
        Assert.Equal(first, second);
        _ = Assert.Single(first);
    }

    [Fact]
    public void DiscoverApps_UsesExecutableIconCountReader()
    {
        var scanner = new CountingShortcutTraversalScanner(
        [
            new ShortcutTraversalEntry(@"C:\Links\App.lnk", @"C:\Apps\App.exe", null)
        ]);
        var iconReader = new FakeExecutableIconCountReader(new Dictionary<string, int> { [@"C:\Apps\App.exe"] = 0 });
        var service = CreateService(scanner, iconReader);

        var apps = service.DiscoverApps();

        Assert.Contains(apps, a => a.TargetPath == @"C:\Apps\App.exe");
        Assert.Equal(1, scanner.ScanCount);
        Assert.True(iconReader.CallCount > 0);
    }

    [Fact]
    public void DiscoverApps_IncludesWindowsAppsShortcutTargets()
    {
        var scanner = new CountingShortcutTraversalScanner(
        [
            new ShortcutTraversalEntry(
                @"C:\Links\Notepad.lnk",
                @"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2501.1.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe",
                null)
        ]);
        var service = CreateService(scanner);

        var apps = service.DiscoverApps();

        var discovered = Assert.Single(apps);
        Assert.Equal("Notepad", discovered.Name);
    }

    [Fact]
    public void DiscoverApps_DoesNotDuplicateWindowsAppsAppFoundByShortcut()
    {
        const string windowsAppsPath = @"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2501.1.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe";
        var scanner = new CountingShortcutTraversalScanner(
        [
            new ShortcutTraversalEntry(@"C:\Links\Notepad.lnk", windowsAppsPath, null)
        ]);
        var windowsAppsDiscovery = new FakeWindowsAppsAppDiscoveryService(
        [
            new DiscoveredApp("Windows Package Name", windowsAppsPath)
        ]);
        var service = CreateService(scanner, windowsAppsDiscoveryService: windowsAppsDiscovery);

        var apps = service.DiscoverApps();

        var discovered = Assert.Single(apps);
        Assert.Equal("Notepad", discovered.Name);
        Assert.Equal(1, windowsAppsDiscovery.CallCount);
    }

    [Fact]
    public void CacheFiltering_ReusesMaterializedTraversal()
    {
        var scanner = new CountingShortcutTraversalScanner(
        [
            new ShortcutTraversalEntry(@"C:\Links\App.lnk", @"C:\Apps\App.exe", "--flag"),
            new ShortcutTraversalEntry(@"C:\Links\Other.lnk", @"C:\Apps\Other.exe", null)
        ]);
        var service = CreateService(scanner);
        var cache = service.CreateTraversalCache();

        var first = cache.FindWhere((target, _) => string.Equals(target, @"C:\Apps\App.exe", StringComparison.OrdinalIgnoreCase));
        var second = cache.FindWhere((_, args) => args == null);

        Assert.Equal(1, scanner.ScanCount);
        Assert.Equal([@"C:\Links\App.lnk"], first);
        Assert.Equal([@"C:\Links\Other.lnk"], second);
    }

    [Fact]
    public void DiscoverApps_UsesLiveTraversalAndFiltering()
    {
        var scanner = new CountingShortcutTraversalScanner(
        [
            new ShortcutTraversalEntry(@"C:\Links\App.lnk", @"C:\Apps\App.exe", null),
            new ShortcutTraversalEntry(@"C:\Links\App Duplicate.lnk", @"C:\Apps\App.exe", null),
            new ShortcutTraversalEntry(@"C:\Links\Doc.lnk", @"C:\Docs\readme.txt", null),
            new ShortcutTraversalEntry(@"C:\Links\Uninstall App.lnk", @"C:\Apps\unins000.exe", null),
            new ShortcutTraversalEntry(@"C:\Links\System.lnk", @"C:\Windows\system32\sfc.exe", null)
        ]);
        var service = CreateService(scanner);

        var apps = service.DiscoverApps();

        Assert.Equal(1, scanner.ScanCount);
        var discovered = Assert.Single(apps, a => a.TargetPath == @"C:\Apps\App.exe");
        Assert.Equal("App", discovered.Name);
        Assert.DoesNotContain(apps, a => a.TargetPath == @"C:\Docs\readme.txt");
        Assert.DoesNotContain(apps, a => a.TargetPath == @"C:\Apps\unins000.exe");
        Assert.DoesNotContain(apps, a => a.TargetPath == @"C:\Windows\system32\sfc.exe");
    }

    private static ShortcutDiscoveryService CreateService(
        IShortcutTraversalScanner scanner,
        FakeExecutableIconCountReader? iconReader = null,
        IWindowsAppsAppDiscoveryService? windowsAppsDiscoveryService = null)
        => new(scanner, new Mock<IDatabaseProvider>().Object, iconReader ?? new FakeExecutableIconCountReader(new Dictionary<string, int>
        {
            ["C:\\Apps\\App.exe"] = 1,
            ["C:\\Apps\\Other.exe"] = 1,
            ["C:\\Apps\\Folder\\App.exe"] = 1,
            ["C:\\Windows\\system32\\sfc.exe"] = 1,
            [@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2501.1.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe"] = 1
        }), windowsAppsDiscoveryService ?? new FakeWindowsAppsAppDiscoveryService([]));

    private sealed class CountingShortcutTraversalScanner(IReadOnlyList<ShortcutTraversalEntry> entries)
        : IShortcutTraversalScanner
    {
        public int ScanCount { get; private set; }

        public IEnumerable<ShortcutTraversalEntry> ScanShortcuts(HashSet<string>? managedSids)
        {
            ScanCount++;
            foreach (var entry in entries)
                yield return entry;
        }
    }

    private sealed class FakeExecutableIconCountReader(Dictionary<string, int> iconCounts) : IExecutableIconCountReader
    {
        private readonly Dictionary<string, int> _iconCounts = iconCounts;

        public int CallCount { get; private set; }

        public bool TryGetIconCount(string path, out int iconCount)
        {
            ++CallCount;
            if (_iconCounts.TryGetValue(path, out var count))
            {
                iconCount = count;
                return true;
            }

            iconCount = 0;
            return false;
        }
    }

    private sealed class FakeWindowsAppsAppDiscoveryService(IReadOnlyList<DiscoveredApp> apps) : IWindowsAppsAppDiscoveryService
    {
        private readonly IReadOnlyList<DiscoveredApp> apps = apps;

        public int CallCount { get; private set; }

        public IReadOnlyList<DiscoveredApp> DiscoverApps()
        {
            CallCount++;
            return apps;
        }
    }
}

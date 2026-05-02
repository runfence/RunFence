using Moq;
using RunFence.Apps.Shortcuts;
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

    private static ShortcutDiscoveryService CreateService(IShortcutTraversalScanner scanner)
        => new(scanner, new Mock<IDatabaseProvider>().Object);

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
}

using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AclManagerScanService"/> result types and the grant/traverse classification logic.
///
/// Note: <see cref="AclManagerScanService.ScanAsync"/> requires <c>SeBackupPrivilege</c> (elevated process),
/// so the actual scan operation cannot be integration-tested without elevation.
/// These tests cover the result record types and the ScanResult invariant enforced during post-processing.
/// </summary>
public class AclManagerScanServiceTests
{
    [Fact]
    public void ScanResult_GrantPathsAndTraversePaths_AreDisjointByConstruction()
    {
        // The ScanResult is constructed from post-processed data where grant paths are excluded
        // from the traverse list. Verify the contract holds when built manually.
        var grantPath = @"C:\Foo\Bar";
        var traverseOnlyPath = @"C:\Foo";
        var grantPaths = new List<(string Path, bool IsDeny)> { (grantPath, false) };
        var traversePaths = new List<string> { traverseOnlyPath }; // parent of grant, not the grant itself
        var result = new ScanResult(grantPaths, traversePaths, []);

        var grantSet = new HashSet<string>(result.GrantPaths.Select(g => g.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var tp in result.TraversePaths)
            Assert.DoesNotContain(tp, grantSet);
    }

    // --- DiscoveredRights dictionary is case-insensitive ---

    [Fact]
    public void ScanResult_DiscoveredRights_CaseInsensitiveLookup()
    {
        var rights = new Dictionary<string, DiscoveredGrantRights>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Foo\Bar"] = new(false, false, false, false, false, false, false)
        };
        var result = new ScanResult([], [], rights);

        // Case-insensitive key lookup
        Assert.True(result.DiscoveredRights.ContainsKey(@"C:\FOO\BAR"));
        Assert.True(result.DiscoveredRights.ContainsKey(@"c:\foo\bar"));
    }

    // --- GrantPaths distinguishes allow from deny mode ---

    [Fact]
    public void ScanResult_GrantPaths_AllowAndDenyAreDistinctEntries()
    {
        // Same path can appear twice: once for allow ACE, once for deny ACE
        var grantPaths = new List<(string Path, bool IsDeny)>
        {
            (@"C:\Target", false), // allow
            (@"C:\Target", true) // deny
        };
        var result = new ScanResult(grantPaths, [], []);

        Assert.Equal(2, result.GrantPaths.Count);
        Assert.Contains((@"C:\Target", false), result.GrantPaths);
        Assert.Contains((@"C:\Target", true), result.GrantPaths);
    }
}
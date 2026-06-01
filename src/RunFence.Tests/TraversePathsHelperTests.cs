using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class TraversePathsHelperTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    // --- TrackPath ---

    [Fact]
    public void TrackPath_NewPath_NormalizesStoredPath()
    {
        var traversePaths = new List<GrantedPathEntry>();

        TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\..\Foo\Bar", []);

        Assert.Equal(@"C:\Foo\Bar", traversePaths[0].Path);
    }

    [Theory]
    [InlineData(@"C:\Foo\Bar")]
    [InlineData(@"c:\foo\bar")]
    public void TrackPath_DuplicatePath_ReturnsFalseAndDoesNotAddEntry(string duplicate)
    {
        var traversePaths = new List<GrantedPathEntry>();
        TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\Bar", []);

        var result = TraversePathsHelper.TrackPath(traversePaths, duplicate, []);

        Assert.False(result);
        Assert.Single(traversePaths);
    }

    [Fact]
    public void TrackPath_DuplicatePathWithFreshAppliedPaths_RefreshesStoredAppliedPaths()
    {
        var traversePaths = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Foo\Bar", IsTraverseOnly = true, AllAppliedPaths = [@"C:\Old"] }
        };

        var result = TraversePathsHelper.TrackPath(
            traversePaths,
            @"C:\Foo\Bar",
            [@"C:\Foo\Bar", @"C:\Foo", @"C:\"]);

        Assert.True(result);
        Assert.Equal([@"C:\Foo\Bar", @"C:\Foo", @"C:\"], Assert.Single(traversePaths).AllAppliedPaths);
    }

    [Fact]
    public void TrackPath_DuplicatePathWithEquivalentAppliedPaths_ReturnsFalse()
    {
        var traversePaths = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Foo\Bar", IsTraverseOnly = true, AllAppliedPaths = [@"C:\Foo\Bar", @"C:\Foo"] }
        };

        var result = TraversePathsHelper.TrackPath(
            traversePaths,
            @"C:\Foo\Bar",
            [@"c:\foo\bar", @"c:\foo"]);

        Assert.False(result);
        Assert.Equal([@"C:\Foo\Bar", @"C:\Foo"], Assert.Single(traversePaths).AllAppliedPaths);
    }

    [Fact]
    public void TrackPath_EmptyAppliedPaths_AllAppliedPathsIsNull()
    {
        var traversePaths = new List<GrantedPathEntry>();

        TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\Bar", []);

        Assert.Null(traversePaths[0].AllAppliedPaths);
    }

    [Fact]
    public void TrackPath_TrackedSourceSid_NewSpecificContainerEntryStoresSource()
    {
        var traversePaths = new List<GrantedPathEntry>();

        TraversePathsHelper.TrackPath(
            traversePaths,
            @"C:\Foo\Bar",
            [],
            trackedSourceSid: "S-1-15-2-99-1-2-3-4-5-6");

        var entry = Assert.Single(traversePaths);
        Assert.Contains("S-1-15-2-99-1-2-3-4-5-6", entry.SourceSids ?? []);
    }

    [Fact]
    public void TrackPath_TrackedSourceSid_DoesNotConvertManualSharedEntry()
    {
        var traversePaths = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Foo\Bar", IsTraverseOnly = true, AllAppliedPaths = [@"C:\Old"], SourceSids = null }
        };

        var changed = TraversePathsHelper.TrackPath(
            traversePaths,
            @"C:\Foo\Bar",
            [@"C:\Foo\Bar", @"C:\Foo"],
            trackedSourceSid: "S-1-15-2-99-1-2-3-4-5-6");

        Assert.False(changed);
        var entry = Assert.Single(traversePaths);
        Assert.Null(entry.SourceSids);
        Assert.Equal([@"C:\Old"], entry.AllAppliedPaths);
    }

    [Fact]
    public void TrackPath_TrackedSourceSid_ExistingTrackedEntryRefreshesAppliedPaths()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var traversePaths = new List<GrantedPathEntry>
        {
            new()
            {
                Path = @"C:\Foo\Bar",
                IsTraverseOnly = true,
                AllAppliedPaths = [@"C:\Old"],
                SourceSids = [containerSid]
            }
        };

        var changed = TraversePathsHelper.TrackPath(
            traversePaths,
            @"C:\Foo\Bar",
            [@"C:\Foo\Bar", @"C:\Foo"],
            trackedSourceSid: containerSid);

        Assert.True(changed);
        var entry = Assert.Single(traversePaths);
        Assert.Equal([@"C:\Foo\Bar", @"C:\Foo"], entry.AllAppliedPaths);
        Assert.Equal([containerSid], entry.SourceSids);
    }

    // --- CollectAncestorPaths ---

    [Fact]
    public void CollectAncestorPaths_CollectsFullChainUpToRoot()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TraversePathsHelper.CollectAncestorPaths(@"C:\Foo\Bar\Baz", result);

        Assert.Contains(@"C:\Foo\Bar\Baz", result);
        Assert.Contains(@"C:\Foo\Bar", result);
        Assert.Contains(@"C:\Foo", result);
        Assert.Contains(@"C:\", result);
    }

    [Fact]
    public void CollectAncestorPaths_DriveRoot_ContainsOnlyRoot()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TraversePathsHelper.CollectAncestorPaths(@"C:\", result);

        Assert.Single(result);
        Assert.Contains(@"C:\", result);
    }

    [Fact]
    public void CollectAncestorPaths_AddsToExistingSet()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Other" };

        TraversePathsHelper.CollectAncestorPaths(@"C:\Foo\Bar", result);

        Assert.Contains(@"D:\Other", result);
        Assert.Contains(@"C:\Foo\Bar", result);
        Assert.Contains(@"C:\Foo", result);
        Assert.Contains(@"C:\", result);
    }

    [Fact]
    public void CollectAncestorPaths_TwoPaths_SameAncestorsNotDuplicated()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TraversePathsHelper.CollectAncestorPaths(@"C:\Foo\A", result);
        TraversePathsHelper.CollectAncestorPaths(@"C:\Foo\B", result);

        // C:\ and C:\Foo appear once each (HashSet deduplication)
        Assert.Single(result, p => p.Equals(@"C:\", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result, p => p.Equals(@"C:\Foo", StringComparison.OrdinalIgnoreCase));
    }

    // --- GetOrCreateTraversePaths ---

    [Fact]
    public void GetOrCreateTraversePaths_MissingSid_CreatesAccountEntryWithEmptyGrants()
    {
        var db = new AppDatabase();

        var entries = TraversePathsHelper.GetOrCreateTraversePaths(db, TestSid);

        Assert.NotNull(entries);
        Assert.Empty(entries);
        Assert.NotNull(db.GetAccount(TestSid));
    }

    // --- GetTraversePaths ---

    [Fact]
    public void GetTraversePaths_MissingSid_DoesNotMutateAccounts()
    {
        var db = new AppDatabase();
        int accountCount = db.Accounts.Count;

        TraversePathsHelper.GetTraversePaths(db, TestSid);

        Assert.Equal(accountCount, db.Accounts.Count); // No account created for missing SID
    }
}

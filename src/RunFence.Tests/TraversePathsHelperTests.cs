using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class TraversePathsHelperTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string OtherSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    // --- TrackPath ---

    [Fact]
    public void TrackPath_NewPath_ReturnsTrueAddsEntryAndSetsIsTraverseOnly()
    {
        var traversePaths = new List<GrantedPathEntry>();

        var result = TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\Bar", []);

        Assert.True(result);
        Assert.Single(traversePaths);
        Assert.True(traversePaths[0].IsTraverseOnly);
    }

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
    public void TrackPath_WithAppliedPaths_StoresThemOnEntry()
    {
        var traversePaths = new List<GrantedPathEntry>();
        var appliedPaths = new List<string> { @"C:\Foo\Bar", @"C:\Foo", @"C:\" };

        TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\Bar", appliedPaths);

        Assert.Equal(appliedPaths, traversePaths[0].AllAppliedPaths);
    }

    [Fact]
    public void TrackPath_EmptyAppliedPaths_AllAppliedPathsIsNull()
    {
        var traversePaths = new List<GrantedPathEntry>();

        TraversePathsHelper.TrackPath(traversePaths, @"C:\Foo\Bar", []);

        Assert.Null(traversePaths[0].AllAppliedPaths);
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

    [Fact]
    public void GetOrCreateTraversePaths_ExistingEntry_ReturnsSameList()
    {
        var db = new AppDatabase();
        var first = TraversePathsHelper.GetOrCreateTraversePaths(db, TestSid);
        first.Add(new GrantedPathEntry { Path = @"C:\Foo", IsTraverseOnly = true });

        var second = TraversePathsHelper.GetOrCreateTraversePaths(db, TestSid);

        Assert.Same(first, second);
        Assert.Single(second);
    }

    [Fact]
    public void GetOrCreateTraversePaths_DifferentSids_IndependentLists()
    {
        var db = new AppDatabase();

        var entries1 = TraversePathsHelper.GetOrCreateTraversePaths(db, TestSid);
        var entries2 = TraversePathsHelper.GetOrCreateTraversePaths(db, OtherSid);

        Assert.NotSame(entries1, entries2);
    }

    // --- GetTraversePaths ---

    [Fact]
    public void GetTraversePaths_MissingSid_ReturnsEmptyList()
    {
        var db = new AppDatabase();

        var result = TraversePathsHelper.GetTraversePaths(db, TestSid);

        Assert.Empty(result);
    }

    [Fact]
    public void GetTraversePaths_ExistingSid_ReturnsEntries()
    {
        var db = new AppDatabase();
        var expected = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Foo", IsTraverseOnly = true }
        };
        db.GetOrCreateAccount(TestSid).Grants = expected;

        var result = TraversePathsHelper.GetTraversePaths(db, TestSid);

        Assert.Same(expected, result);
    }

    [Fact]
    public void GetTraversePaths_MissingSid_DoesNotMutateAccounts()
    {
        var db = new AppDatabase();
        int accountCount = db.Accounts.Count;

        TraversePathsHelper.GetTraversePaths(db, TestSid);

        Assert.Equal(accountCount, db.Accounts.Count); // No account created for missing SID
    }
}
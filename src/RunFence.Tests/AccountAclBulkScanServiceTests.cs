using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Unit tests for <see cref="AccountAclBulkScanService"/>.
/// Uses a stub <see cref="IFileSystemAclTraverser"/> to bypass real NTFS traversal and privilege requirements.
/// </summary>
public class AccountAclBulkScanServiceTests
{
    private const string Sid1 = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string UnknownSid = "S-1-5-21-9999999-9999999-9999999-9999";

    private static readonly FileSystemRights TraverseOnlyRights =
        FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;

    private static AccountAclBulkScanService CreateService(
        IEnumerable<(string Path, FileSystemSecurity Security)> entries)
        => new(new StubTraverser(entries));

    /// <summary>
    /// Builds a <see cref="DirectorySecurity"/> with explicit ACEs for the given SID.
    /// Owner can optionally be set to one of the SIDs.
    /// </summary>
    private static DirectorySecurity MakeSecurity(
        IEnumerable<(string Sid, FileSystemRights Rights, AccessControlType Type)> rules,
        string? ownerSid = null)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false); // remove inherited ACEs

        if (ownerSid != null)
            security.SetOwner(new SecurityIdentifier(ownerSid));

        foreach (var (sid, rights, type) in rules)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                type));
        }

        return security;
    }

    // --- Unknown SIDs excluded ---

    [Fact]
    public async Task Scan_UnknownSids_ExcludedFromResults()
    {
        var security = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        // Only ask for UnknownSid — the Sid1 ACE should be ignored
        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UnknownSid };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.DoesNotContain(UnknownSid, (IDictionary<string, AccountScanResult>)result);
    }

    [Fact]
    public async Task Scan_SidNotInKnownSids_ExcludedEvenIfAceExists()
    {
        var security = MakeSecurity([
            (Sid1, FileSystemRights.ReadData, AccessControlType.Allow),
            (Sid2, FileSystemRights.ReadData, AccessControlType.Allow)
        ]);
        var service = CreateService([(@"C:\Foo", security)]);

        // Only Sid1 is known; Sid2 should be excluded
        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        Assert.DoesNotContain(Sid2, (IDictionary<string, AccountScanResult>)result);
    }

    // --- Allow ACE classification ---

    [Fact]
    public async Task Scan_AllowAce_AppearsInGrantsAsDenyFalse()
    {
        var security = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single();
        Assert.False(grant.IsDeny);
        Assert.Equal(@"C:\Foo", grant.Path, StringComparer.OrdinalIgnoreCase);
    }

    // --- Deny ACE classification ---

    [Fact]
    public async Task Scan_DenyAce_AppearsInGrantsAsDenyTrue()
    {
        var security = MakeSecurity([(Sid1, FileSystemRights.WriteData, AccessControlType.Deny)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single();
        Assert.True(grant.IsDeny);
        Assert.True(grant.Write);
    }

    // --- Traverse-only ACE classification ---

    [Fact]
    public async Task Scan_TraverseOnlyAce_AppearsInTraversePaths_NotGrants()
    {
        var security = MakeSecurity([(Sid1, TraverseOnlyRights, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var scanResult = result[Sid1];
        Assert.Contains(scanResult.TraversePaths, p =>
            string.Equals(p, @"C:\Foo", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(scanResult.Grants);
    }

    [Fact]
    public async Task Scan_TraversePathAlsoHasFullGrant_ExcludedFromTraversePaths()
    {
        // Both a traverse-only and a full-grant ACE on the same path → classified as grant only
        var security = MakeSecurity([
            (Sid1, TraverseOnlyRights, AccessControlType.Allow),
            (Sid1, FileSystemRights.ReadData, AccessControlType.Allow)
        ]);
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        var scanResult = result[Sid1];
        // The path has a full grant ACE so it's not in TraversePaths
        Assert.DoesNotContain(scanResult.TraversePaths, p =>
            string.Equals(p, @"C:\Foo", StringComparison.OrdinalIgnoreCase));
        // But it IS in Grants
        Assert.Contains(scanResult.Grants, g => !g.IsDeny);
    }

    // --- Discovered rights mapping ---

    [Fact]
    public async Task Scan_DiscoveredRights_ReadAndExecuteSetCorrectly()
    {
        // Use ReadAndExecute which includes both Read and Execute masks
        var security = MakeSecurity([(Sid1, FileSystemRights.ReadAndExecute, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        // ReadAndExecute has more than just TraverseOnlyMask, so it's a full grant
        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        Assert.True(grant.Read);
        Assert.True(grant.Execute);
        Assert.False(grant.Write);
        Assert.False(grant.Special);
    }

    [Fact]
    public async Task Scan_DiscoveredRights_WriteSetCorrectly()
    {
        // Use Modify which includes Write + Read rights (a realistic combination)
        var security = MakeSecurity([(Sid1, FileSystemRights.Modify, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        Assert.True(grant.Write);
        Assert.True(grant.Read);
        Assert.True(grant.Execute);
    }

    [Fact]
    public async Task Scan_DiscoveredRights_SpecialSetCorrectly()
    {
        // FullControl includes all rights including ChangePermissions (Special)
        var security = MakeSecurity([(Sid1, FileSystemRights.FullControl, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        Assert.True(grant.Special);
        Assert.True(grant.Read);
        Assert.True(grant.Write);
        Assert.True(grant.Execute);
    }

    // --- Owner detection ---

    [Fact]
    public async Task Scan_SidIsOwner_IsOwnerTrue()
    {
        var security = MakeSecurity(
            [(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)],
            ownerSid: Sid1);
        var service = CreateService([(@"C:\Foo", security)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        Assert.True(grant.IsOwner);
    }

    [Fact]
    public async Task Scan_SidIsNotOwner_IsOwnerFalse()
    {
        var security = MakeSecurity(
            [(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)],
            ownerSid: Sid2);
        var service = CreateService([(@"C:\Foo", security)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        Assert.False(grant.IsOwner);
    }

    // --- Multiple paths and SIDs ---

    [Fact]
    public async Task Scan_MultiplePaths_AllClassified()
    {
        var security1 = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var security2 = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security1), (@"C:\Bar", security2)]);

        var result = await service.ScanAllAccountsAsync(
            @"C:\", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        Assert.Equal(2, result[Sid1].Grants.Count);
    }

    [Fact]
    public async Task Scan_MultipleSids_IndependentResults()
    {
        var security = MakeSecurity([
            (Sid1, FileSystemRights.ReadData, AccessControlType.Allow),
            (Sid2, FileSystemRights.WriteData, AccessControlType.Allow)
        ]);
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1, Sid2 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.True(result[Sid1].Grants.Single(g => !g.IsDeny).Read);
        Assert.True(result[Sid2].Grants.Single(g => !g.IsDeny).Write);
    }

    [Fact]
    public async Task Scan_SidWithNoAces_NotInResults()
    {
        var security = MakeSecurity([]); // no ACEs
        var service = CreateService([(@"C:\Foo", security)]);

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };
        var result = await service.ScanAllAccountsAsync(
            @"C:\Foo", knownSids, new Progress<long>(), CancellationToken.None);

        Assert.DoesNotContain(Sid1, (IDictionary<string, AccountScanResult>)result);
    }

    // --- Cancellation ---

    [Fact]
    public async Task Scan_PreCancelledToken_ThrowsOrReturnsEmpty()
    {
        // Arrange: a pre-cancelled token so any cancellation-aware code exits immediately
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var security = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var service = CreateService([(@"C:\Foo", security)]);
        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 };

        // Act: either returns gracefully with empty results or throws OperationCanceledException
        try
        {
            var result = await service.ScanAllAccountsAsync(
                @"C:\Foo", knownSids, new Progress<long>(), cts.Token);
            // If it returns (didn't throw), it must produce an empty result due to cancellation
            Assert.Empty(result);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation propagated
        }
    }

    // --- File-vs-directory branching (SpecialMask selection) ---

    [Fact]
    public async Task Scan_DirectoryPath_UsesSpecialFolderMask_IncludesDelete()
    {
        // SpecialFolderMask includes Delete; SpecialFileMask does not.
        // Only Delete (without other write bits) triggers Special on folders but not on files.
        // Use a real temp directory so Directory.Exists returns true for the scanned path.
        using var tempDir = new TempDirectory();
        var deleteOnlyRights = FileSystemRights.Delete;
        var security = MakeSecurity([(Sid1, deleteOnlyRights, AccessControlType.Allow)]);
        var service = new AccountAclBulkScanService(new StubTraverser([(tempDir.Path, security)]));

        var result = await service.ScanAllAccountsAsync(
            tempDir.Path, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        // Directory branch: Delete is part of SpecialFolderMask → Special = true
        Assert.True(grant.Special, "Directory path should use SpecialFolderMask (includes Delete)");
    }

    [Fact]
    public async Task Scan_FilePath_UsesSpecialFileMask_DeleteNotSpecial()
    {
        // SpecialFileMask does NOT include Delete; for files Delete is part of WriteFileMask.
        // Use a real temp file so Directory.Exists returns false for the scanned path.
        using var tempDir = new TempDirectory();
        var tempFile = Path.Combine(tempDir.Path, "test.txt");
        File.WriteAllText(tempFile, "");

        var deleteOnlyRights = FileSystemRights.Delete;
        var security = MakeSecurity([(Sid1, deleteOnlyRights, AccessControlType.Allow)]);
        var service = new AccountAclBulkScanService(new StubTraverser([(tempFile, security)]));

        var result = await service.ScanAllAccountsAsync(
            tempFile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1 },
            new Progress<long>(), CancellationToken.None);

        Assert.Contains(Sid1, (IDictionary<string, AccountScanResult>)result);
        var grant = result[Sid1].Grants.Single(g => !g.IsDeny);
        // File branch: Delete is part of WriteFileMask, not SpecialFileMask → Special = false, Write = true
        Assert.False(grant.Special, "File path should use SpecialFileMask (Delete is Write, not Special)");
        Assert.True(grant.Write, "File path: Delete right maps to Write for files");
    }

    // --- Strict cancellation contract ---

    [Fact]
    public async Task Scan_CancelledMidScan_DoesNotIncludeItemsAfterCancellation()
    {
        // Arrange: traverser yields 2 entries; cancels after the first one is produced.
        var cts = new CancellationTokenSource();
        var security1 = MakeSecurity([(Sid1, FileSystemRights.ReadData, AccessControlType.Allow)]);
        var security2 = MakeSecurity([(Sid2, FileSystemRights.ReadData, AccessControlType.Allow)]);

        // CancellingTraverser yields the first entry, then cancels the token and throws.
        var service = new AccountAclBulkScanService(
            new CancellingTraverser([security1, security2], cts));

        var knownSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sid1, Sid2 };

        // Act: either throws OperationCanceledException or returns partial results
        Dictionary<string, AccountScanResult>? result = null;
        try
        {
            result = await service.ScanAllAccountsAsync(
                @"C:\Root", knownSids, new Progress<long>(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: cancellation propagated as exception — no items after cancel
            return;
        }

        // If it returned normally, Sid2 must NOT be present (it was scanned after cancellation)
        Assert.DoesNotContain(Sid2, (IDictionary<string, AccountScanResult>)result!);
    }

    /// <summary>
    /// In-memory stub that returns a fixed set of (path, security) pairs without touching the filesystem.
    /// </summary>
    private sealed class StubTraverser(
        IEnumerable<(string Path, FileSystemSecurity Security)> entries) : IFileSystemAclTraverser
    {
        public IEnumerable<AclTraversalEntry> Traverse(
            IReadOnlyList<string> rootPaths, IProgress<long> progress, CancellationToken ct)
            => entries.Select(e => new AclTraversalEntry(e.Path, false, e.Security));
    }

    /// <summary>
    /// Traverser that yields the first entry normally, then cancels the token and throws
    /// <see cref="OperationCanceledException"/> to simulate mid-scan cancellation.
    /// Each entry is returned at a synthetic path so Directory.Exists returns false (file branch).
    /// </summary>
    private sealed class CancellingTraverser(
        IReadOnlyList<FileSystemSecurity> entries,
        CancellationTokenSource cts) : IFileSystemAclTraverser
    {
        public IEnumerable<AclTraversalEntry> Traverse(
            IReadOnlyList<string> rootPaths, IProgress<long> progress, CancellationToken ct)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = $@"C:\Root\Item{i}.txt";
                yield return new AclTraversalEntry(path, false, entries[i]);
                if (i == 0)
                    cts.Cancel();
            }
        }
    }
}
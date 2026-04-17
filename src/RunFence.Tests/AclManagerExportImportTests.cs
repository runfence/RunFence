using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;
using static RunFence.Acl.UI.AclManagerExportImport;

namespace RunFence.Tests;

/// <summary>
/// Tests for the import processing logic of <see cref="AclManagerExportImport"/>.
/// Export (UI + file) is not tested here; tested via the data model serialization round-trip.
/// </summary>
public class AclManagerExportImportTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppConfigService> _appConfig = new();
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();

    private AclManagerExportImport CreateImport(AppDatabase? db = null, AclManagerPendingChanges? pending = null)
    {
        db ??= new AppDatabase();
        pending ??= new AclManagerPendingChanges();
        var dbProvider = new LambdaDatabaseProvider(() => db);
        var exportImport = new AclManagerExportImport(_pathGrantService.Object, _aclPermission.Object, _log.Object, dbProvider);
        exportImport.Initialize(pending, TestSid, isContainer: false, owner: new NullWin32Window());
        return exportImport;
    }

    // --- Serialization format ---

    [Fact]
    public void ExportData_Serialization_HasNoSidField_VersionOne_CorrectRightsMapping()
    {
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: true)],
            Traverse: [new ExportTraverseEntry(@"C:\Bar")]);

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(data, options);

        // No SID field
        Assert.DoesNotContain("sid", json, StringComparison.OrdinalIgnoreCase);
        // Version = 1
        Assert.Contains("\"version\":1", json);
        // Rights mapped correctly
        Assert.Contains("\"execute\":true", json);
        Assert.Contains("\"write\":false", json);
        Assert.Contains("\"read\":true", json);
        Assert.Contains("\"special\":false", json);
        Assert.Contains("\"owner\":true", json);
        Assert.Contains("\"isDeny\":false", json);
    }

    [Fact]
    public void ExportData_RoundTrip_PreservesAllFields()
    {
        var original = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Apps\Tool.exe", IsDeny: true, Execute: true, Write: true, Read: true, Special: false, Owner: false)],
            Traverse: [new ExportTraverseEntry(@"C:\Apps")]);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(original, options);
        var restored = JsonSerializer.Deserialize<ExportData>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.Version);
        Assert.Single(restored.Grants!);
        Assert.Equal(@"C:\Apps\Tool.exe", restored.Grants![0].Path);
        Assert.True(restored.Grants[0].IsDeny);
        Assert.True(restored.Grants[0].Execute);
        Assert.True(restored.Grants[0].Write);
        Assert.True(restored.Grants[0].Read);
        Assert.False(restored.Grants[0].Special);
        Assert.False(restored.Grants[0].Owner);
        Assert.Single(restored.Traverse!);
        Assert.Equal(@"C:\Apps", restored.Traverse![0].Path);
    }

    [Fact]
    public void SavedRightsState_SerializationRoundTrip()
    {
        // SavedRightsState is a plain record; verify it survives JSON serialization
        var original = new SavedRightsState(Execute: true, Write: false, Read: true, Special: true, Own: false);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SavedRightsState>(json);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    // --- ProcessImport: grants go to PendingAdds ---

    [Fact]
    public void ProcessImport_GrantEntries_AddedToPendingAdds()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        Assert.True(pending.IsPendingAdd(normalizedPath, isDeny: false));
        var entry = pending.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Read);
        Assert.True(refreshCalled);
    }

    [Fact]
    public void ProcessImport_TraverseEntries_AddedToPendingTraverseAdds()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        importer.ProcessImport(data, () => { });

        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        Assert.True(pending.IsPendingTraverseAdd(normalizedPath));
    }

    [Fact]
    public void ProcessImport_AllowGrant_AutoCreatesTraverseForParent()
    {
        // When an allow grant is imported and there's no traverse in the file or DB, auto-create one
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        var parentPath = Path.GetFullPath(@"C:\Foo\Bar");
        Assert.True(pending.IsPendingTraverseAdd(parentPath));
    }

    [Fact]
    public void ProcessImport_DenyGrant_DoesNotAutoCreateTraverse()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", IsDeny: true, false, true, false, true, false)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        var parentPath = Path.GetFullPath(@"C:\Foo\Bar");
        Assert.False(pending.IsPendingTraverseAdd(parentPath));
    }

    [Fact]
    public void ProcessImport_TraverseInFile_NoAutoCreateDuplicate()
    {
        // If the import file already has a traverse entry for the parent, don't create another
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var parentPath = @"C:\Foo\Bar";
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", false, false, false, true, false, false)],
            Traverse: [new ExportTraverseEntry(parentPath)]);

        importer.ProcessImport(data, () => { });

        var normalizedParent = Path.GetFullPath(parentPath);
        // Traverse is added once (from explicit traverse entry), not twice (from auto-creation)
        Assert.True(pending.IsPendingTraverseAdd(normalizedParent));
        Assert.Single(pending.PendingTraverseAdds);
    }

    [Fact]
    public void ProcessImport_DeduplicatesAgainstExistingDbEntries()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var parentPath = Path.GetFullPath(@"C:\Foo");
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.AddRange([
            new GrantedPathEntry { Path = normalizedPath, IsDeny = false },
            // Traverse for parent also already present → prevents auto-traverse creation
            new GrantedPathEntry { Path = parentPath, IsTraverseOnly = true }
        ]);
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(db, pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        // Grant already in DB → not added to pending
        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: false));
        // Traverse also in DB → not added to pending → nothing added overall → no refresh
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_DeduplicatesAgainstExistingPendingAdds()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var parentPath = Path.GetFullPath(@"C:\Foo");
        var pending = new AclManagerPendingChanges
        {
            PendingAdds =
            {
                [(normalizedPath, false)] = new GrantedPathEntry { Path = normalizedPath }
            },
            PendingTraverseAdds =
            {
                // Traverse for parent also already pending → prevents auto-traverse creation
                [parentPath] = new GrantedPathEntry { Path = parentPath, IsTraverseOnly = true }
            }
        };
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        var addCountBefore = pending.PendingAdds.Count;
        var traverseCountBefore = pending.PendingTraverseAdds.Count;
        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        // Grant already pending → not added again
        Assert.Equal(addCountBefore, pending.PendingAdds.Count);
        // Traverse already pending → not added again
        Assert.Equal(traverseCountBefore, pending.PendingTraverseAdds.Count);
        // Nothing new → no refresh
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_DeduplicatesTraverseAgainstDb()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true });
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(db, pending);
        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        Assert.False(pending.IsPendingTraverseAdd(normalizedPath));
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_EmptyGrantsAndTraverse_RefreshNotCalled()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1, Grants: [], Traverse: []);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        Assert.False(pending.HasPendingChanges);
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_NullGrantsAndTraverse_NoOp()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 1, Grants: null, Traverse: null);

        importer.ProcessImport(data, () => { });

        Assert.False(pending.HasPendingChanges);
    }

    [Fact]
    public void ProcessImport_ExceptionDuringProcessing_RollsBackAllAddedEntries()
    {
        // A grant with a valid path is processed first (staged into PendingAdds).
        // A second entry with an invalid path (contains null character) causes Path.GetFullPath to
        // throw — exercising the catch/rollback block that removes previously-staged entries.
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);

        var validPath = @"C:\Foo\App.exe";
        var data = new ExportData(Version: 1,
            Grants:
            [
                new ExportGrantEntry(validPath, false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                // A path containing a null char causes Path.GetFullPath to throw ArgumentException.
                new ExportGrantEntry("C:\\Bad\0Path\\App.exe", false, Execute: false, Write: false, Read: true, Special: false, Owner: false)
            ],
            Traverse: null);

        // ProcessImport re-throws after rollback — verify the exception propagates.
        Assert.Throws<ArgumentException>(() => importer.ProcessImport(data, () => { }));

        // After rollback, no entry for the first (valid) path must remain in pending.
        var normalizedValid = Path.GetFullPath(validPath);
        Assert.False(pending.IsPendingAdd(normalizedValid, isDeny: false));
        Assert.Empty(pending.PendingAdds);
        Assert.Empty(pending.PendingTraverseAdds);
    }

    [Fact]
    public void ProcessImport_WrongVersion_NoChanges()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(Version: 99,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        Assert.False(pending.HasPendingChanges);
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_GrantPendingRemoval_ImportCancelsRemoval()
    {
        // Scenario: a grant exists in the DB and is queued for removal (PendingRemoves).
        // Importing the same path must cancel the pending removal — NOT add a new pending-add.
        // This ensures the net result is that the grant is preserved (remove cancelled).
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var existingEntry = new GrantedPathEntry { Path = normalizedPath, IsDeny = false };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(existingEntry);
        var pending = new AclManagerPendingChanges
        {
            PendingRemoves =
            {
                // Queue the existing grant for removal (simulates user clicking Remove before importing)
                [(normalizedPath, false)] = existingEntry
            }
        };

        var importer = CreateImport(db, pending);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        // Pending removal must be cancelled (entry stays in DB after Apply)
        Assert.False(pending.IsPendingRemove(normalizedPath, isDeny: false));
        // No new pending-add for an entry that's already in DB
        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: false));
    }

    [Fact]
    public void ProcessImport_TraversePendingRemoval_ImportAddsNewEntry()
    {
        // Traverse is in DB but is pending removal. Importing the same traverse path
        // must NOT add a new pending-add (IsTraverseAlreadyPresent returns true only when
        // NOT pending-remove; since it IS pending removal the path appears absent, so the
        // import adds a fresh pending-add, effectively restoring the traverse).
        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        var traverseEntry = new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(traverseEntry);
        var pending = new AclManagerPendingChanges
        {
            PendingTraverseRemoves =
            {
                // Queue the traverse for removal
                [normalizedPath] = traverseEntry
            }
        };

        var importer = CreateImport(db, pending);
        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        // IsTraverseAlreadyPresent returns false when pending-remove is set,
        // so import treats it as absent and adds a new pending-add.
        Assert.True(pending.IsPendingTraverseAdd(normalizedPath));
        Assert.True(refreshCalled);
    }

    // --- Stub IWin32Window ---

    private sealed class NullWin32Window : IWin32Window
    {
        public nint Handle => nint.Zero;
    }
}
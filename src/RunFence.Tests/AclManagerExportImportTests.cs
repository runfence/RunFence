using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
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
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<ISpecificContainerAceConflictDetector> _specificContainerAceConflictDetector = new();

    private AclManagerExportImport CreateImport(
        AppDatabase? db = null,
        AclManagerPendingChanges? pending = null,
        string sid = TestSid,
        bool isContainer = false)
    {
        db ??= new AppDatabase();
        pending ??= new AclManagerPendingChanges();
        var dbProvider = new LambdaDatabaseProvider(() => db);
        var importProcessor = new AclImportProcessor(
            _log.Object,
            dbProvider,
            new GrantTraversePathResolver(new TestFileSystemPathInfo()),
            _specificContainerAceConflictDetector.Object);
        var exportImport = new AclManagerExportImport(
            _pathGrantService.Object,
            _aclPermission.Object,
            _log.Object,
            dbProvider,
            new TraverseGrantOwnerResolver(),
            importProcessor);
        exportImport.Initialize(pending, sid, isContainer, owner: new NullWin32Window());
        return exportImport;
    }

    private AclImportProcessor CreateImportProcessor(AppDatabase? db = null)
    {
        db ??= new AppDatabase();
        return new AclImportProcessor(
            _log.Object,
            new LambdaDatabaseProvider(() => db),
            new GrantTraversePathResolver(new TestFileSystemPathInfo()),
            _specificContainerAceConflictDetector.Object);
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

    [Fact]
    public void BuildFullExportData_ExportsCommittedPendingAndTraverseEntriesWithExactRights()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\Committed\App.exe",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        });
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\Committed\Traverse",
            IsTraverseOnly = true
        });

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\Pending\Denied.txt", true)] = new GrantedPathEntry
        {
            Path = @"C:\Pending\Denied.txt",
            IsDeny = true,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false)
        };
        pending.PendingTraverseAdds[@"C:\Pending\Traverse"] = new GrantedPathEntry
        {
            Path = @"C:\Pending\Traverse",
            IsTraverseOnly = true
        };

        var exporter = CreateImport(db, pending);

        var exportData = exporter.BuildFullExportData();

        Assert.Equal(1, exportData.Version);
        Assert.Equal(2, exportData.Grants!.Count);
        Assert.Contains(exportData.Grants, grant =>
            grant.Path == @"C:\Committed\App.exe" &&
            !grant.IsDeny &&
            grant.Execute &&
            !grant.Write &&
            grant.Read &&
            !grant.Special &&
            grant.Owner);
        Assert.Contains(exportData.Grants, grant =>
            grant.Path == @"C:\Pending\Denied.txt" &&
            grant.IsDeny &&
            grant.Execute &&
            !grant.Write &&
            grant.Read &&
            !grant.Special &&
            !grant.Owner);
        Assert.Equal(2, exportData.Traverse!.Count);
        Assert.Contains(exportData.Traverse, entry => entry.Path == @"C:\Committed\Traverse");
        Assert.Contains(exportData.Traverse, entry => entry.Path == @"C:\Pending\Traverse");
    }

    [Fact]
    public void BuildFullExportData_SpecificContainer_ExportsOnlyApplicableSharedTraverseEntries()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        const string otherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
        var db = new AppDatabase();
        db.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.AddRange(
        [
            new GrantedPathEntry
            {
                Path = @"C:\Own",
                IsTraverseOnly = true,
                SourceSids = [containerSid]
            },
            new GrantedPathEntry
            {
                Path = @"C:\Manual",
                IsTraverseOnly = true,
                SourceSids = null
            },
            new GrantedPathEntry
            {
                Path = @"C:\Other",
                IsTraverseOnly = true,
                SourceSids = [otherContainerSid]
            }
        ]);

        var exporter = CreateImport(db, sid: containerSid);

        var exportData = exporter.BuildFullExportData();

        Assert.Contains(exportData.Traverse!, entry => entry.Path == @"C:\Own");
        Assert.Contains(exportData.Traverse!, entry => entry.Path == @"C:\Manual");
        Assert.DoesNotContain(exportData.Traverse!, entry => entry.Path == @"C:\Other");
    }

    [Fact]
    public void BuildGrantSelectionExportData_MissingSavedRights_PopulatesFromReadGrantState()
    {
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Selected\App.exe",
            IsDeny = false,
            SavedRights = null
        };
        _aclPermission
            .Setup(permission => permission.ResolveAccountGroupSids(TestSid))
            .Returns(["S-1-1-0"]);
        _pathGrantService
            .Setup(service => service.ReadGrantState(
                entry.Path,
                TestSid,
                It.Is<IReadOnlyList<string>>(groups => groups.SequenceEqual(new[] { "S-1-1-0" })) ))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Checked,
                AllowWrite: RightCheckState.Checked,
                AllowSpecial: RightCheckState.Checked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Checked,
                IsAdminOwner: false,
                DirectAllowAceCount: 1,
                DirectDenyAceCount: 0));

        var exporter = CreateImport();

        var exportData = exporter.BuildGrantSelectionExportData([entry]);

        var grant = Assert.Single(exportData.Grants!);
        Assert.Equal(@"C:\Selected\App.exe", grant.Path);
        Assert.True(grant.Execute);
        Assert.True(grant.Write);
        Assert.True(grant.Read);
        Assert.True(grant.Special);
        Assert.True(grant.Owner);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Write);
        Assert.True(entry.SavedRights.Special);
        Assert.True(entry.SavedRights.Own);
    }

    [Fact]
    public void BuildTraverseSelectionExportData_ExportsOnlySelectedTraverseEntries()
    {
        var traverseEntry = new GrantedPathEntry
        {
            Path = @"C:\Selected\Traverse",
            IsTraverseOnly = true
        };
        var grantEntry = new GrantedPathEntry
        {
            Path = @"C:\Selected\App.exe",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };

        var exporter = CreateImport();

        var exportData = exporter.BuildTraverseSelectionExportData([traverseEntry, grantEntry]);

        Assert.Empty(exportData.Grants!);
        var traverse = Assert.Single(exportData.Traverse!);
        Assert.Equal(@"C:\Selected\Traverse", traverse.Path);
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
    public void ProcessImport_LowIntegrityGrant_IgnoresImportedOwner()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending, sid: AclHelper.LowIntegritySid);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: true, Read: true, Special: true, Owner: true)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var entry = pending.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.False(entry.SavedRights!.Own);
        Assert.True(entry.SavedRights.Execute);
        Assert.True(entry.SavedRights.Write);
        Assert.True(entry.SavedRights.Read);
        Assert.True(entry.SavedRights.Special);
    }

    [Fact]
    public void ProcessImport_ContainerGrant_IgnoresImportedOwner()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending, sid: "S-1-15-2-1", isContainer: true);
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: true, Read: true, Special: true, Owner: true)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var entry = pending.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.False(entry.SavedRights!.Own);
    }

    [Fact]
    public void ProcessImport_LowIntegrityConflictWarning_SkipsGrant()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        _specificContainerAceConflictDetector
            .Setup(d => d.HasExplicitSpecificContainerAce(normalizedPath))
            .Returns(true);

        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending, sid: AclHelper.LowIntegritySid);
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: false));
    }

    [Fact]
    public void ImportProcessor_LowIntegrityConflictWarning_ReturnsStructuredWarning()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        _specificContainerAceConflictDetector
            .Setup(d => d.HasExplicitSpecificContainerAce(normalizedPath))
            .Returns(true);

        var pending = new AclManagerPendingChanges();
        var processor = CreateImportProcessor();
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        var result = processor.ProcessImport(data, pending, AclHelper.LowIntegritySid, isContainer: false);

        Assert.False(result.AnyAdded);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(normalizedPath, warning.Path);
        Assert.Contains("Specific AppContainer ACEs conflict", warning.Message);
        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: false));
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
    public void ProcessImport_OppositeModesInSameImport_RejectsConflictingPath()
    {
        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(pending: pending);
        var data = new ExportData(
            Version: 1,
            Grants:
            [
                new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: true, Execute: true, Write: true, Read: true, Special: true, Owner: false)
            ],
            Traverse: null);

        importer.ProcessImport(data, () => { });

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        Assert.True(pending.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: true));
    }

    [Fact]
    public void ProcessImport_OppositeModeAlreadyEffective_RejectsGrantAndDoesNotAddTraverse()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = normalizedPath, IsDeny = true });

        var pending = new AclManagerPendingChanges();
        var importer = CreateImport(db, pending);
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        Assert.False(pending.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ProcessImport_OppositeModePendingRemoval_AllowsReplacementGrant()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var db = new AppDatabase();
        var denyEntry = new GrantedPathEntry { Path = normalizedPath, IsDeny = true };
        db.GetOrCreateAccount(TestSid).Grants.Add(denyEntry);

        var pending = new AclManagerPendingChanges
        {
            PendingRemoves =
            {
                [(normalizedPath, true)] = denyEntry
            }
        };

        var importer = CreateImport(db, pending);
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        importer.ProcessImport(data, () => refreshCalled = true);

        Assert.True(pending.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.True(refreshCalled);
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
    public void ProcessImport_ExceptionDuringProcessing_RestoresCancelledPendingRemoval()
    {
        // Scenario: a DB entry is in PendingRemoves (user queued removal).
        // The import first encounters this entry (cancels its pending removal), then
        // encounters an invalid second entry that causes an exception.
        // After rollback the cancelled pending removal must be restored.
        var normalizedGood = Path.GetFullPath(@"C:\Foo\App.exe");
        var existingEntry = new GrantedPathEntry { Path = normalizedGood, IsDeny = false };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(existingEntry);
        var pending = new AclManagerPendingChanges
        {
            PendingRemoves = { [(normalizedGood, false)] = existingEntry }
        };

        var importer = CreateImport(db, pending);

        var data = new ExportData(Version: 1,
            Grants:
            [
                // This entry is in DB + PendingRemoves: import will cancel the pending removal.
                new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                // Invalid path causes Path.GetFullPath to throw, triggering rollback.
                new ExportGrantEntry("C:\\Bad\0Path\\App.exe", false, Execute: false, Write: false, Read: true, Special: false, Owner: false)
            ],
            Traverse: null);

        Assert.Throws<ArgumentException>(() => importer.ProcessImport(data, () => { }));

        // Rollback must restore the cancelled pending removal.
        Assert.True(pending.IsPendingRemove(normalizedGood, isDeny: false));
    }

    [Fact]
    public void ProcessImport_ExceptionDuringProcessing_RestoresCancelledUntrackAndTraverseRemoval()
    {
        var normalizedGrant = Path.GetFullPath(@"C:\Foo\App.exe");
        var normalizedTraverse = Path.GetFullPath(@"C:\Foo");
        var grantEntry = new GrantedPathEntry { Path = normalizedGrant, IsDeny = false };
        var traverseEntry = new GrantedPathEntry { Path = normalizedTraverse, IsTraverseOnly = true };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.AddRange([grantEntry, traverseEntry]);

        var pending = new AclManagerPendingChanges
        {
            PendingUntrackGrants = { [(normalizedGrant, false)] = grantEntry },
            PendingTraverseRemoves = { [normalizedTraverse] = traverseEntry },
            PendingUntrackTraverse = { [normalizedTraverse] = traverseEntry }
        };

        var importer = CreateImport(db, pending);
        var data = new ExportData(
            Version: 1,
            Grants:
            [
                new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                new ExportGrantEntry("C:\\Bad\0Path\\App.exe", false, Execute: false, Write: false, Read: true, Special: false, Owner: false)
            ],
            Traverse: [new ExportTraverseEntry(@"C:\Foo")]);

        Assert.Throws<ArgumentException>(() => importer.ProcessImport(data, () => { }));

        Assert.True(pending.IsUntrackGrant(normalizedGrant, false));
        Assert.True(pending.IsPendingTraverseRemove(normalizedTraverse));
        Assert.True(pending.IsUntrackTraverse(normalizedTraverse));
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
    public void ProcessImport_TraversePendingRemoval_ImportCancelsRemoval()
    {
        // Traverse is in DB but is pending removal. Importing the same traverse path must
        // cancel the pending removal and keep the committed DB entry instead of queuing a
        // duplicate pending add.
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

        Assert.False(pending.IsPendingTraverseRemove(normalizedPath));
        Assert.False(pending.IsPendingTraverseAdd(normalizedPath));
        Assert.True(refreshCalled);
    }

    // --- Stub IWin32Window ---

    private sealed class NullWin32Window : IWin32Window
    {
        public nint Handle => nint.Zero;
    }
}

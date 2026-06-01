using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Acl.UI.ImportExport;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using System.Windows.Forms;
using Xunit;
using ExportData = RunFence.Acl.UI.ImportExport.AclExportData;
using ExportGrantEntry = RunFence.Acl.UI.ImportExport.AclExportGrantEntry;
using ExportTraverseEntry = RunFence.Acl.UI.ImportExport.AclExportTraverseEntry;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AclManagerExportImport"/> wrapper behavior and
/// <see cref="AclImportProcessor"/> import processing behavior.
/// </summary>
public class AclManagerExportImportTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IGrantInspectionService> _grantInspectionService = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<ISpecificContainerAceConflictDetector> _specificContainerAceConflictDetector = new();
    private readonly Mock<IFileContentService> _fileContentService = new();
    private readonly Mock<IMessageBoxService> _messageBoxService = new();

    private static AclGrantPendingChangesSnapshot GetGrantSnapshot(AclManagerPendingChanges pending)
        => pending.Grants.GetSnapshot();

    private static AclTraversePendingChangesSnapshot GetTraverseSnapshot(AclManagerPendingChanges pending)
        => pending.Traverse.GetSnapshot();

    private static void AddPendingGrant(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.AddGrant(entry);

    private static void AddPendingTraverse(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.AddTraverse(entry);

    private static void AddPendingRemoval(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.MarkGrantForRemoval(entry);

    private static void AddPendingTraverseRemoval(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.MarkTraverseForRemoval(entry);

    private static void AddPendingUntrackGrant(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.UntrackGrant(entry);

    private static void AddPendingUntrackTraverse(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.UntrackTraverse(entry);

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
            _grantInspectionService.Object,
            _aclPermission.Object,
            _log.Object,
            dbProvider,
            new TraverseGrantOwnerResolver(),
            importProcessor,
            _fileContentService.Object,
            new FakeOpenFileDialogAdapterFactory(),
            new FakeSaveFileDialogAdapterFactory(),
            _messageBoxService.Object);
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

    private AclImportResult ProcessImport(
        ExportData data,
        AclManagerPendingChanges pending,
        AppDatabase? db = null,
        string sid = TestSid,
        bool isContainer = false,
        Action? refreshGrids = null)
    {
        var result = CreateImportProcessor(db).ProcessImport(new AclImportRequest(data, pending, sid, isContainer));
        if (result.AnyAdded)
            refreshGrids?.Invoke();
        return result;
    }

    // --- Serialization format ---

    [Fact]
    public void ExportData_Serialization_HasNoSidField_VersionOne_CorrectRightsMapping()
    {
        var data = new AclExportData(
            Version: 1,
            Grants: [new AclExportGrantEntry(@"C:\Foo", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: true)],
            Traverse: [new AclExportTraverseEntry(@"C:\Bar")]);

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
        Assert.Contains("\"path\":\"C:\\\\Foo\"", json);
        Assert.Contains("\"isDeny\":false", json);
        Assert.Contains("\"execute\":true", json);
        Assert.Contains("\"write\":false", json);
        Assert.Contains("\"read\":true", json);
        Assert.Contains("\"special\":false", json);
        Assert.Contains("\"owner\":true", json);
    }

    [Fact]
    public void ExportData_SerializationRoundTrip_PreservesGrantTraverseAndRights()
    {
        var data = new AclExportData(
            Version: 1,
            Grants: [new AclExportGrantEntry(@"C:\Foo\App.exe", IsDeny: true, Execute: false, Write: true, Read: false, Special: true, Owner: false)],
            Traverse: [new AclExportTraverseEntry(@"C:\Foo\Sub")]);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(data, options);
        var deserialized = JsonSerializer.Deserialize<ExportData>(json, options)!;

        Assert.Equal(1, deserialized.Version);
        var grant = Assert.Single(deserialized.Grants!);
        var traverse = Assert.Single(deserialized.Traverse!);
        Assert.Equal(@"C:\Foo\App.exe", grant.Path);
        Assert.True(grant.IsDeny);
        Assert.False(grant.Execute);
        Assert.True(grant.Write);
        Assert.False(grant.Read);
        Assert.True(grant.Special);
        Assert.False(grant.Owner);
        Assert.Equal(@"C:\Foo\Sub", traverse.Path);
    }

    [Fact]
    public void SavedRightsState_SerializationRoundTrip_PreservesAllFields()
    {
        var expected = new SavedRightsState(
            Execute: true,
            Write: false,
            Read: true,
            Special: true,
            Own: true);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(expected, options);
        var deserialized = JsonSerializer.Deserialize<SavedRightsState>(json, options);

        Assert.Equal(expected, deserialized);
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
        AddPendingGrant(pending, new GrantedPathEntry
        {
            Path = @"C:\Pending\Denied.txt",
            IsDeny = true,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false)
        });
        AddPendingTraverse(pending, new GrantedPathEntry
        {
            Path = @"C:\Pending\Traverse",
            IsTraverseOnly = true
        });

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
    public void BuildFullExportData_SpecificContainer_OmitsTraverseEntries()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var db = new AppDatabase();
        db.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry
            {
                Path = @"C:\Own",
                IsTraverseOnly = true,
                SourceSids = [containerSid]
            });

        var exporter = CreateImport(db, sid: containerSid, isContainer: true);

        var exportData = exporter.BuildFullExportData();

        Assert.Empty(exportData.Traverse!);
    }

    [Fact]
    public void BuildFullExportData_SharedAppContainerOwnerWithoutContainerMode_ExportsOnlyApplicableSharedTraverseEntries()
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
        _grantInspectionService
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

    [Fact]
    public void BuildTraverseSelectionExportData_SpecificContainer_OmitsSelectedTraverseEntries()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var traverseEntry = new GrantedPathEntry
        {
            Path = @"C:\Selected\Traverse",
            IsTraverseOnly = true
        };

        var exporter = CreateImport(new AppDatabase(), sid: containerSid, isContainer: true);

        var exportData = exporter.BuildTraverseSelectionExportData([traverseEntry]);

        Assert.Empty(exportData.Traverse!);
    }

    [Fact]
    public async Task Export_WithPendingChangesAndPromptCancelled_AbortsBeforeApply()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var applyCalls = 0;
        var context = CreateExportWrapperContext(
            pending: pending,
            promptResult: DialogResult.Cancel,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(true);
            });

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Equal(0, applyCalls);
        Assert.Single(context.MessageBoxCalls);
        Assert.Contains("There are unapplied changes.", context.MessageBoxCalls[0].Text);
        Assert.Equal(0, context.SaveDialog.ShowCount);
        _fileContentService.Verify(
            service => service.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()),
            Times.Never);
    }

    [Fact]
    public async Task Export_WithPendingChangesAndSuccessfulApply_ContinuesToSaveDialog()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var applyCalls = 0;
        var context = CreateExportWrapperContext(
            pending: pending,
            saveDialogResult: DialogResult.Cancel,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(true);
            });

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Equal(1, applyCalls);
        Assert.Single(context.MessageBoxCalls);
        Assert.Equal(1, context.SaveDialog.ShowCount);
        _fileContentService.Verify(
            service => service.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()),
            Times.Never);
    }

    [Fact]
    public async Task Export_WithPendingChangesAndFailedApply_StopsBeforeSaveDialog()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var applyCalls = 0;
        var context = CreateExportWrapperContext(
            pending: pending,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(false);
            });

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Equal(1, applyCalls);
        Assert.Single(context.MessageBoxCalls);
        Assert.Equal(0, context.SaveDialog.ShowCount);
        _fileContentService.Verify(
            service => service.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()),
            Times.Never);
    }

    [Fact]
    public async Task Export_WithNoEntries_ShowsNothingToExportMessage()
    {
        var context = CreateExportWrapperContext();

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        var message = Assert.Single(context.MessageBoxCalls);
        Assert.Equal("Nothing to export.", message.Text);
        Assert.Equal("Export Grants", message.Caption);
        Assert.Equal(0, context.SaveDialog.ShowCount);
        _fileContentService.Verify(
            service => service.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()),
            Times.Never);
    }

    [Fact]
    public async Task Export_WhenSaveDialogCancelled_DoesNotWriteFile()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var applyCalls = 0;
        var context = CreateExportWrapperContext(
            pending: pending,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(true);
            },
            saveDialogResult: DialogResult.Cancel);

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Single(context.MessageBoxCalls);
        Assert.Contains("There are unapplied changes.", context.MessageBoxCalls[0].Text);
        Assert.Equal(1, applyCalls);
        Assert.Equal(1, context.SaveDialog.ShowCount);
        _fileContentService.Verify(
            service => service.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>()),
            Times.Never);
    }

    [Fact]
    public async Task Export_WhenSaveDialogAccepted_WritesSerializedFile()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var savePath = @"C:\Export\grants.rfg";
        var applyCalls = 0;
        var context = CreateExportWrapperContext(
            pending: pending,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(true);
            },
            saveDialogResult: DialogResult.OK,
            saveFileName: savePath);

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Single(context.MessageBoxCalls);
        Assert.Equal(1, applyCalls);
        Assert.Contains("There are unapplied changes.", context.MessageBoxCalls[0].Text);
        Assert.Equal(1, context.SaveDialog.ShowCount);
        _fileContentService.Verify(service => service.WriteAllText(
                savePath,
                It.Is<string>(json =>
                    json.Contains("\"version\": 1", StringComparison.Ordinal) &&
                    json.Contains("\"path\": \"C:\\\\Pending\\\\App.exe\"", StringComparison.Ordinal)),
                Encoding.UTF8),
            Times.Once);
    }

    [Fact]
    public async Task Export_WhenWriteFails_ShowsErrorAndLogs()
    {
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, CreateExportableGrant(@"C:\Pending\App.exe"));
        var savePath = @"C:\Export\grants.rfg";
        var exception = new InvalidOperationException("disk full");
        var applyCalls = 0;
        _fileContentService
            .Setup(service => service.WriteAllText(savePath, It.IsAny<string>(), Encoding.UTF8))
            .Throws(exception);
        var context = CreateExportWrapperContext(
            pending: pending,
            applyAsync: () =>
            {
                applyCalls++;
                return Task.FromResult(true);
            },
            saveDialogResult: DialogResult.OK,
            saveFileName: savePath);

        await context.ExportImport.Export(CreateExportGrid(), CreateExportGrid(), grantsTabActive: true);

        Assert.Equal(2, context.MessageBoxCalls.Count);
        Assert.Equal("Export", context.MessageBoxCalls[0].Caption);
        Assert.Equal("There are unapplied changes. Apply them before exporting?\n\nClick OK to apply first, or Cancel to abort.", context.MessageBoxCalls[0].Text);
        Assert.Equal("Export Grants", context.MessageBoxCalls[1].Caption);
        Assert.Equal("Export failed: disk full", context.MessageBoxCalls[1].Text);
        Assert.Equal(1, applyCalls);
        _log.Verify(log => log.Error($"Failed to export grants to '{savePath}'", exception), Times.Once);
    }

    // --- ProcessImport: grants go to PendingAdds ---

    [Fact]
    public void ImportProcessor_GrantEntries_AddedToPendingAdds()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        ProcessImport(data, pending, refreshGrids: () => refreshCalled = true);

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        Assert.True(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
        var entry = pending.Grants.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Read);
        Assert.True(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_LowIntegrityGrant_IgnoresImportedOwner()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: true, Read: true, Special: true, Owner: true)],
            Traverse: null);

        ProcessImport(data, pending, sid: AclHelper.LowIntegritySid);

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var entry = pending.Grants.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.False(entry.SavedRights!.Own);
        Assert.True(entry.SavedRights.Execute);
        Assert.True(entry.SavedRights.Write);
        Assert.True(entry.SavedRights.Read);
        Assert.True(entry.SavedRights.Special);
    }

    [Fact]
    public void ImportProcessor_ContainerGrant_IgnoresImportedOwner()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: true, Read: true, Special: true, Owner: true)],
            Traverse: null);

        ProcessImport(data, pending, sid: "S-1-15-2-1", isContainer: true);

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var entry = pending.Grants.FindPendingAdd(normalizedPath, isDeny: false);
        Assert.NotNull(entry);
        Assert.False(entry.SavedRights!.Own);
    }

    [Fact]
    public void ImportProcessor_LowIntegrityConflictWarning_SkipsGrant()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        _specificContainerAceConflictDetector
            .Setup(d => d.HasExplicitSpecificContainerAce(normalizedPath))
            .Returns(true);

        var pending = new AclManagerPendingChanges();
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        ProcessImport(data, pending, sid: AclHelper.LowIntegritySid);

        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
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

        var result = processor.ProcessImport(new AclImportRequest(data, pending, AclHelper.LowIntegritySid, IsContainer: false));

        Assert.False(result.AnyAdded);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(normalizedPath, warning.Path);
        Assert.Contains("Specific AppContainer ACEs conflict", warning.Message);
        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
    }

    [Fact]
    public void ImportWrapper_CancelledDialog_DoesNothing()
    {
        var context = CreateWrapperContext(dialogResult: DialogResult.Cancel);

        context.ExportImport.Import(context.RefreshGrids);

        Assert.False(context.RefreshCalled);
        Assert.Empty(context.MessageBoxCalls);
        context.ImportProcessor.Verify(
            processor => processor.ProcessImport(It.IsAny<AclImportRequest>()),
            Times.Never);
    }

    [Fact]
    public void ImportWrapper_InvalidJsonRead_ShowsReadError()
    {
        var context = CreateWrapperContext(fileText: "{not-json");

        context.ExportImport.Import(context.RefreshGrids);

        Assert.False(context.RefreshCalled);
        var message = Assert.Single(context.MessageBoxCalls);
        Assert.Contains("Import failed:", message.Text);
        context.ImportProcessor.Verify(
            processor => processor.ProcessImport(It.IsAny<AclImportRequest>()),
            Times.Never);
    }

    [Fact]
    public void ImportWrapper_UnsupportedVersion_ShowsErrorWithoutCallingProcessor()
    {
        var context = CreateWrapperContext(fileText: "{\"version\":99,\"grants\":[],\"traverse\":[]}");

        context.ExportImport.Import(context.RefreshGrids);

        Assert.False(context.RefreshCalled);
        var message = Assert.Single(context.MessageBoxCalls);
        Assert.Equal("Unsupported grants file version: 99", message.Text);
        context.ImportProcessor.Verify(
            processor => processor.ProcessImport(It.IsAny<AclImportRequest>()),
            Times.Never);
    }

    [Fact]
    public void ImportWrapper_ValidData_ForwardsParsedRequestAndRefreshesOnlyWhenAnyAdded()
    {
        var pending = new AclManagerPendingChanges();
        var importResult = new AclImportResult(true, []);
        var context = CreateWrapperContext(
            pending: pending,
            fileText: "{\"version\":1,\"grants\":[{\"path\":\"C:\\\\Apps\\\\Tool.exe\",\"isDeny\":false,\"execute\":true,\"write\":false,\"read\":true,\"special\":false,\"owner\":false}],\"traverse\":[]}",
            importResult: importResult);

        context.ExportImport.Import(context.RefreshGrids);

        Assert.True(context.RefreshCalled);
        Assert.Empty(context.MessageBoxCalls);
        context.ImportProcessor.Verify(processor => processor.ProcessImport(
            It.Is<AclImportRequest>(request =>
                ReferenceEquals(request.Pending, pending) &&
                request.Sid == TestSid &&
                !request.IsContainer &&
                request.ExportData.Version == 1 &&
                AssertSingleGrant(request.ExportData.Grants!, @"C:\Apps\Tool.exe"))),
            Times.Once);
    }

    [Fact]
    public void ImportWrapper_Warnings_ShowsSingleWarningDialog()
    {
        var context = CreateWrapperContext(
            fileText: "{\"version\":1,\"grants\":[],\"traverse\":[]}",
            importResult: new AclImportResult(true, [new AclImportWarning(@"C:\Apps", "Skipped")]));

        context.ExportImport.Import(context.RefreshGrids);

        Assert.True(context.RefreshCalled);
        var message = Assert.Single(context.MessageBoxCalls);
        Assert.Equal(MessageBoxIcon.Warning, message.Icon);
        Assert.Contains(@"C:\Apps: Skipped", message.Text);
    }

    [Fact]
    public void ImportWrapper_NoNewEntries_ShowsNoOpMessageWithoutRefresh()
    {
        var context = CreateWrapperContext(
            fileText: "{\"version\":1,\"grants\":[],\"traverse\":[]}",
            importResult: new AclImportResult(false, []));

        context.ExportImport.Import(context.RefreshGrids);

        Assert.False(context.RefreshCalled);
        var message = Assert.Single(context.MessageBoxCalls);
        Assert.Equal(MessageBoxIcon.Information, message.Icon);
        Assert.Equal("No new entries to import (all paths already exist).", message.Text);
    }

    [Fact]
    public void ImportProcessor_TraverseEntries_AddedToPendingTraverseAdds()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        ProcessImport(data, pending);

        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        Assert.True(pending.Traverse.IsPendingTraverseAdd(normalizedPath));
    }

    [Fact]
    public void ImportProcessor_AllowGrant_AutoCreatesTraverseForParent()
    {
        // When an allow grant is imported and there's no traverse in the file or DB, auto-create one
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        ProcessImport(data, pending);

        var parentPath = Path.GetFullPath(@"C:\Foo\Bar");
        Assert.True(pending.Traverse.IsPendingTraverseAdd(parentPath));
    }

    [Fact]
    public void ImportProcessor_DenyGrant_DoesNotAutoCreateTraverse()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", IsDeny: true, false, true, false, true, false)],
            Traverse: null);

        ProcessImport(data, pending);

        var parentPath = Path.GetFullPath(@"C:\Foo\Bar");
        Assert.False(pending.Traverse.IsPendingTraverseAdd(parentPath));
    }

    [Fact]
    public void ImportProcessor_TraverseInFile_NoAutoCreateDuplicate()
    {
        // If the import file already has a traverse entry for the parent, don't create another
        var pending = new AclManagerPendingChanges();
        var parentPath = @"C:\Foo\Bar";
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\Bar\App.exe", false, false, false, true, false, false)],
            Traverse: [new ExportTraverseEntry(parentPath)]);

        ProcessImport(data, pending);

        var normalizedParent = Path.GetFullPath(parentPath);
        // Traverse is added once (from explicit traverse entry), not twice (from auto-creation)
        Assert.True(pending.Traverse.IsPendingTraverseAdd(normalizedParent));
        Assert.Single(GetTraverseSnapshot(pending).PendingAdds);
    }

    [Fact]
    public void ImportProcessor_DeduplicatesAgainstExistingDbEntries()
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
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        bool refreshCalled = false;
        ProcessImport(data, pending, db, refreshGrids: () => refreshCalled = true);

        // Grant already in DB → not added to pending
        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
        // Traverse also in DB → not added to pending → nothing added overall → no refresh
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_OppositeModesInSameImport_RejectsConflictingPath()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(
            Version: 1,
            Grants:
            [
                new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: true, Execute: true, Write: true, Read: true, Special: true, Owner: false)
            ],
            Traverse: null);

        ProcessImport(data, pending);

        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        Assert.True(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: true));
    }

    [Fact]
    public void ImportProcessor_OppositeModeAlreadyEffective_RejectsGrantAndDoesNotAddTraverse()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = normalizedPath, IsDeny = true });

        var pending = new AclManagerPendingChanges();
        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        ProcessImport(data, pending, db, refreshGrids: () => refreshCalled = true);

        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_OppositeModePendingRemoval_AllowsReplacementGrant()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var db = new AppDatabase();
        var denyEntry = new GrantedPathEntry { Path = normalizedPath, IsDeny = true };
        db.GetOrCreateAccount(TestSid).Grants.Add(denyEntry);

        var pending = new AclManagerPendingChanges();
        AddPendingRemoval(pending, denyEntry);

        var data = new ExportData(
            Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        bool refreshCalled = false;
        ProcessImport(data, pending, db, refreshGrids: () => refreshCalled = true);

        Assert.True(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
        Assert.True(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_DeduplicatesAgainstExistingPendingAdds()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var parentPath = Path.GetFullPath(@"C:\Foo");
        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, new GrantedPathEntry { Path = normalizedPath });
        AddPendingTraverse(pending, new GrantedPathEntry { Path = parentPath, IsTraverseOnly = true });
        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        var addCountBefore = GetGrantSnapshot(pending).PendingAdds.Count;
        var traverseCountBefore = GetTraverseSnapshot(pending).PendingAdds.Count;
        bool refreshCalled = false;
        ProcessImport(data, pending, refreshGrids: () => refreshCalled = true);

        // Grant already pending → not added again
        Assert.Equal(addCountBefore, GetGrantSnapshot(pending).PendingAdds.Count);
        // Traverse already pending → not added again
        Assert.Equal(traverseCountBefore, GetTraverseSnapshot(pending).PendingAdds.Count);
        // Nothing new → no refresh
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_DeduplicatesTraverseAgainstDb()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true });
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        bool refreshCalled = false;
        ProcessImport(data, pending, db, refreshGrids: () => refreshCalled = true);

        Assert.False(pending.Traverse.IsPendingTraverseAdd(normalizedPath));
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_EmptyGrantsAndTraverse_RefreshNotCalled()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1, Grants: [], Traverse: []);

        bool refreshCalled = false;
        ProcessImport(data, pending, refreshGrids: () => refreshCalled = true);

        Assert.False(pending.HasPendingChanges);
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_NullGrantsAndTraverse_NoOp()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 1, Grants: null, Traverse: null);

        ProcessImport(data, pending);

        Assert.False(pending.HasPendingChanges);
    }

    [Fact]
    public void ImportProcessor_ExceptionDuringProcessing_RollsBackAllAddedEntries()
    {
        // A grant with a valid path is processed first (staged into PendingAdds).
        // A second entry with an invalid path (contains null character) causes Path.GetFullPath to
        // throw — exercising the catch/rollback block that removes previously-staged entries.
        var pending = new AclManagerPendingChanges();

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
        Assert.Throws<ArgumentException>(() => ProcessImport(data, pending));

        // After rollback, no entry for the first (valid) path must remain in pending.
        var normalizedValid = Path.GetFullPath(validPath);
        Assert.False(pending.Grants.IsPendingAdd(normalizedValid, isDeny: false));
        Assert.Empty(GetGrantSnapshot(pending).PendingAdds);
        Assert.Empty(GetTraverseSnapshot(pending).PendingAdds);
    }

    [Fact]
    public void ImportProcessor_ExceptionDuringProcessing_RestoresCancelledPendingRemoval()
    {
        // Scenario: a DB entry is in PendingRemoves (user queued removal).
        // The import first encounters this entry (cancels its pending removal), then
        // encounters an invalid second entry that causes an exception.
        // After rollback the cancelled pending removal must be restored.
        var normalizedGood = Path.GetFullPath(@"C:\Foo\App.exe");
        var existingEntry = new GrantedPathEntry { Path = normalizedGood, IsDeny = false };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(existingEntry);
        var pending = new AclManagerPendingChanges();
        AddPendingRemoval(pending, existingEntry);

        var data = new ExportData(Version: 1,
            Grants:
            [
                // This entry is in DB + PendingRemoves: import will cancel the pending removal.
                new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                // Invalid path causes Path.GetFullPath to throw, triggering rollback.
                new ExportGrantEntry("C:\\Bad\0Path\\App.exe", false, Execute: false, Write: false, Read: true, Special: false, Owner: false)
            ],
            Traverse: null);

        Assert.Throws<ArgumentException>(() => ProcessImport(data, pending, db));

        // Rollback must restore the cancelled pending removal.
        Assert.True(pending.Grants.IsPendingRemove(normalizedGood, isDeny: false));
    }

    [Fact]
    public void ImportProcessor_ExceptionDuringProcessing_RestoresCancelledUntrackAndTraverseRemoval()
    {
        var normalizedGrant = Path.GetFullPath(@"C:\Foo\App.exe");
        var normalizedTraverse = Path.GetFullPath(@"C:\Foo");
        var grantEntry = new GrantedPathEntry { Path = normalizedGrant, IsDeny = false };
        var traverseEntry = new GrantedPathEntry { Path = normalizedTraverse, IsTraverseOnly = true };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.AddRange([grantEntry, traverseEntry]);

        var pending = new AclManagerPendingChanges();
        AddPendingUntrackGrant(pending, grantEntry);
        AddPendingTraverseRemoval(pending, traverseEntry);
        AddPendingUntrackTraverse(pending, traverseEntry);

        var data = new ExportData(
            Version: 1,
            Grants:
            [
                new ExportGrantEntry(@"C:\Foo\App.exe", false, Execute: true, Write: false, Read: true, Special: false, Owner: false),
                new ExportGrantEntry("C:\\Bad\0Path\\App.exe", false, Execute: false, Write: false, Read: true, Special: false, Owner: false)
            ],
            Traverse: [new ExportTraverseEntry(@"C:\Foo")]);

        Assert.Throws<ArgumentException>(() => ProcessImport(data, pending, db));

        Assert.True(pending.Grants.IsUntrackGrant(normalizedGrant, false));
        Assert.True(pending.Traverse.IsPendingTraverseRemove(normalizedTraverse));
        Assert.True(pending.Traverse.IsUntrackTraverse(normalizedTraverse));
    }

    [Fact]
    public void ImportProcessor_WrongVersion_NoChanges()
    {
        var pending = new AclManagerPendingChanges();
        var data = new ExportData(Version: 99,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", false, false, false, true, false, false)],
            Traverse: null);

        bool refreshCalled = false;
        ProcessImport(data, pending, refreshGrids: () => refreshCalled = true);

        Assert.False(pending.HasPendingChanges);
        Assert.False(refreshCalled);
    }

    [Fact]
    public void ImportProcessor_GrantPendingRemoval_ImportCancelsRemoval()
    {
        // Scenario: a grant exists in the DB and is queued for removal (PendingRemoves).
        // Importing the same path must cancel the pending removal — NOT add a new pending-add.
        // This ensures the net result is that the grant is preserved (remove cancelled).
        var normalizedPath = Path.GetFullPath(@"C:\Foo\App.exe");
        var existingEntry = new GrantedPathEntry { Path = normalizedPath, IsDeny = false };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(existingEntry);
        var pending = new AclManagerPendingChanges();
        AddPendingRemoval(pending, existingEntry);

        var data = new ExportData(Version: 1,
            Grants: [new ExportGrantEntry(@"C:\Foo\App.exe", IsDeny: false, Execute: true, Write: false, Read: true, Special: false, Owner: false)],
            Traverse: null);

        ProcessImport(data, pending, db);

        // Pending removal must be cancelled (entry stays in DB after Apply)
        Assert.False(pending.Grants.IsPendingRemove(normalizedPath, isDeny: false));
        // No new pending-add for an entry that's already in DB
        Assert.False(pending.Grants.IsPendingAdd(normalizedPath, isDeny: false));
    }

    [Fact]
    public void ImportProcessor_TraversePendingRemoval_ImportCancelsRemoval()
    {
        // Traverse is in DB but is pending removal. Importing the same traverse path must
        // cancel the pending removal and keep the committed DB entry instead of queuing a
        // duplicate pending add.
        var normalizedPath = Path.GetFullPath(@"C:\Parent");
        var traverseEntry = new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true };
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(traverseEntry);
        var pending = new AclManagerPendingChanges();
        AddPendingTraverseRemoval(pending, traverseEntry);

        var data = new ExportData(Version: 1,
            Grants: null,
            Traverse: [new ExportTraverseEntry(@"C:\Parent")]);

        bool refreshCalled = false;
        ProcessImport(data, pending, db, refreshGrids: () => refreshCalled = true);

        Assert.False(pending.Traverse.IsPendingTraverseRemove(normalizedPath));
        Assert.False(pending.Traverse.IsPendingTraverseAdd(normalizedPath));
        Assert.True(refreshCalled);
    }

    // --- Stub IWin32Window ---

    private sealed class NullWin32Window : IWin32Window
    {
        public nint Handle => nint.Zero;
    }

    private WrapperTestContext CreateWrapperContext(
        AclManagerPendingChanges? pending = null,
        string fileText = "{\"version\":1,\"grants\":[],\"traverse\":[]}",
        AclImportResult? importResult = null,
        DialogResult dialogResult = DialogResult.OK)
    {
        pending ??= new AclManagerPendingChanges();
        var openDialog = new FakeOpenFileDialogAdapter(dialogResult, @"C:\Import\grants.rfg");
        var messageBoxCalls = new List<MessageBoxCall>();
        var importProcessor = new Mock<IAclImportProcessor>();

        if (importResult != null)
            importProcessor.Setup(processor => processor.ProcessImport(It.IsAny<AclImportRequest>())).Returns(importResult);

        _fileContentService.Reset();
        _fileContentService
            .Setup(service => service.ReadAllText(openDialog.Dialog.FileName, Encoding.UTF8))
            .Returns(fileText);

        _messageBoxService.Reset();
        _messageBoxService
            .Setup(service => service.Show(
                It.IsAny<IWin32Window?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageBoxButtons>(),
                It.IsAny<MessageBoxIcon>()))
            .Callback<IWin32Window?, string, string, MessageBoxButtons, MessageBoxIcon>((_, text, caption, buttons, icon) =>
                messageBoxCalls.Add(new MessageBoxCall(text, caption, buttons, icon)))
            .Returns(DialogResult.OK);

        var exportImport = new AclManagerExportImport(
            _grantInspectionService.Object,
            _aclPermission.Object,
            _log.Object,
            new LambdaDatabaseProvider(() => new AppDatabase()),
            new TraverseGrantOwnerResolver(),
            importProcessor.Object,
            _fileContentService.Object,
            new FakeOpenFileDialogAdapterFactory(openDialog),
            new FakeSaveFileDialogAdapterFactory(),
            _messageBoxService.Object);
        exportImport.Initialize(pending, TestSid, isContainer: false, owner: new NullWin32Window());

        var refreshCalled = false;
        return new WrapperTestContext(
            exportImport,
            importProcessor,
            () => refreshCalled = true,
            () => refreshCalled,
            messageBoxCalls);
    }

    private ExportWrapperTestContext CreateExportWrapperContext(
        AppDatabase? db = null,
        AclManagerPendingChanges? pending = null,
        Func<Task<bool>>? applyAsync = null,
        DialogResult promptResult = DialogResult.OK,
        DialogResult saveDialogResult = DialogResult.Cancel,
        string saveFileName = @"C:\Export\grants.rfg",
        bool isContainer = false)
    {
        db ??= new AppDatabase();
        pending ??= new AclManagerPendingChanges();
        var saveDialog = new FakeSaveFileDialogAdapter(saveDialogResult, saveFileName);
        var messageBoxCalls = new List<MessageBoxCall>();

        _messageBoxService.Reset();
        _messageBoxService
            .Setup(service => service.Show(
                It.IsAny<IWin32Window?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageBoxButtons>(),
                It.IsAny<MessageBoxIcon>()))
            .Callback<IWin32Window?, string, string, MessageBoxButtons, MessageBoxIcon>((_, text, caption, buttons, icon) =>
                messageBoxCalls.Add(new MessageBoxCall(text, caption, buttons, icon)))
            .Returns(promptResult);

        var exportImport = new AclManagerExportImport(
            _grantInspectionService.Object,
            _aclPermission.Object,
            _log.Object,
            new LambdaDatabaseProvider(() => db),
            new TraverseGrantOwnerResolver(),
            new Mock<IAclImportProcessor>(MockBehavior.Strict).Object,
            _fileContentService.Object,
            new FakeOpenFileDialogAdapterFactory(),
            new FakeSaveFileDialogAdapterFactory(saveDialog),
            _messageBoxService.Object);
        exportImport.Initialize(pending, TestSid, isContainer, owner: new NullWin32Window(), applyAsync);

        return new ExportWrapperTestContext(exportImport, saveDialog, messageBoxCalls);
    }

    private static bool AssertSingleGrant(IReadOnlyList<ExportGrantEntry> grants, string expectedPath)
    {
        if (grants.Count != 1)
            return false;

        var grant = grants[0];
        return grant.Path == expectedPath &&
               !grant.IsDeny &&
               grant.Execute &&
               !grant.Write &&
               grant.Read &&
               !grant.Special &&
               !grant.Owner;
    }

    private static GrantedPathEntry CreateExportableGrant(string path) => new()
    {
        Path = path,
        IsDeny = false,
        SavedRights = SavedRightsState.DefaultForMode(false) with
        {
            Execute = true,
            Read = true
        }
    };

    private static DataGridView CreateExportGrid()
        => new() { AllowUserToAddRows = false };

    private sealed record WrapperTestContext(
        AclManagerExportImport ExportImport,
        Mock<IAclImportProcessor> ImportProcessor,
        Action RefreshGrids,
        Func<bool> GetRefreshCalled,
        List<MessageBoxCall> MessageBoxCalls)
    {
        public bool RefreshCalled => GetRefreshCalled();
    }

    private sealed record ExportWrapperTestContext(
        AclManagerExportImport ExportImport,
        FakeSaveFileDialogAdapter SaveDialog,
        List<MessageBoxCall> MessageBoxCalls);

    private sealed record MessageBoxCall(string Text, string Caption, MessageBoxButtons Buttons, MessageBoxIcon Icon);

    private sealed class FakeOpenFileDialogAdapterFactory(FakeOpenFileDialogAdapter? adapter = null) : IOpenFileDialogAdapterFactory
    {
        private readonly FakeOpenFileDialogAdapter _adapter = adapter ?? new FakeOpenFileDialogAdapter(DialogResult.Cancel, string.Empty);

        public IOpenFileDialogAdapter Create() => _adapter;
    }

    private sealed class FakeOpenFileDialogAdapter(DialogResult dialogResult, string fileName) : IOpenFileDialogAdapter
    {
        public OpenFileDialog Dialog { get; } = new() { FileName = fileName };

        public DialogResult ShowDialog(IWin32Window? owner) => dialogResult;

        public void Dispose() => Dialog.Dispose();
    }

    private sealed class FakeSaveFileDialogAdapterFactory : ISaveFileDialogAdapterFactory
    {
        private readonly FakeSaveFileDialogAdapter _adapter;

        public FakeSaveFileDialogAdapterFactory(FakeSaveFileDialogAdapter? adapter = null)
        {
            _adapter = adapter ?? new FakeSaveFileDialogAdapter(DialogResult.Cancel, string.Empty);
        }

        public ISaveFileDialogAdapter Create() => _adapter;
    }

    private sealed class FakeSaveFileDialogAdapter(DialogResult dialogResult, string fileName) : ISaveFileDialogAdapter
    {
        public SaveFileDialog Dialog { get; } = new() { FileName = fileName };

        public int ShowCount { get; private set; }

        public DialogResult ShowDialog(IWin32Window? owner)
        {
            ShowCount++;
            return dialogResult;
        }

        public void Dispose() => Dialog.Dispose();
    }
}

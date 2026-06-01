using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI.Forms;
using RunFence.Acl.UI.ImportExport;
using RunFence.Apps.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles Export and Import of grant configurations for <see cref="AclManagerDialog"/>.
/// Export produces account-agnostic JSON (no SID). Import delegates to <see cref="AclImportProcessor"/>.
/// </summary>
public class AclManagerExportImport(
    IGrantInspectionService grantInspectionService,
    IAclPermissionService aclPermission,
    ILoggingService log,
    IDatabaseProvider databaseProvider,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    IAclImportProcessor importProcessor,
    IFileContentService fileContentService,
    IOpenFileDialogAdapterFactory openFileDialogFactory,
    ISaveFileDialogAdapterFactory saveFileDialogFactory,
    IMessageBoxService messageBoxService)
{
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private bool _isContainer;
    private IWin32Window _owner = null!;
    private Func<Task<bool>>? _applyAsync;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Initialize(
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        IWin32Window owner,
        Func<Task<bool>>? applyAsync = null)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
        _owner = owner;
        _applyAsync = applyAsync;
    }

    /// <summary>
    /// Exports grants to a JSON file. The exported content depends on selection and active tab:
    /// nothing selected → export ALL; grants tab selection → selected grants only;
    /// traverse tab selection → selected traverse entries only.
    /// </summary>
    public async Task Export(DataGridView grantsGrid, DataGridView traverseGrid, bool grantsTabActive)
    {
        if (_pending.HasPendingChanges && _applyAsync != null)
        {
            var result = messageBoxService.Show(
                _owner,
                "There are unapplied changes. Apply them before exporting?\n\nClick OK to apply first, or Cancel to abort.",
                "Export",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel)
                return;
            if (!await _applyAsync())
                return;
        }
        bool grantsSelected = grantsTabActive && grantsGrid.SelectedRows.Count > 0
                                              && grantsGrid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is GrantedPathEntry);
        bool traverseSelected = !grantsTabActive && traverseGrid.SelectedRows.Count > 0
                                                 && traverseGrid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is GrantedPathEntry);

        AclExportData exportData;

        if (grantsSelected)
        {
            exportData = BuildGrantSelectionExportData(
                grantsGrid.SelectedRows.Cast<DataGridViewRow>()
                    .Select(row => row.Tag)
                    .OfType<GrantedPathEntry>());
        }
        else if (traverseSelected)
        {
            exportData = BuildTraverseSelectionExportData(
                traverseGrid.SelectedRows.Cast<DataGridViewRow>()
                    .Select(row => row.Tag)
                    .OfType<GrantedPathEntry>());
        }
        else
        {
            // No selection on the active tab: export everything from both tabs — by design.
            exportData = BuildFullExportData();
        }

        if (!HasExportEntries(exportData))
        {
            messageBoxService.Show(_owner, "Nothing to export.", "Export Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var saveDialogAdapter = saveFileDialogFactory.Create();
        var sfd = saveDialogAdapter.Dialog;
        sfd.Title = "Export Grants";
        sfd.Filter = "RunFence Grants (*.rfg)|*.rfg|JSON files (*.json)|*.json";
        sfd.DefaultExt = "rfg";
        if (saveDialogAdapter.ShowDialog(_owner) != DialogResult.OK)
            return;

        try
        {
            var json = JsonSerializer.Serialize(exportData, JsonOptions);
            fileContentService.WriteAllText(sfd.FileName, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to export grants to '{sfd.FileName}'", ex);
            messageBoxService.Show(_owner, $"Export failed: {ex.Message}", "Export Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Imports grants from a JSON file. Grant entries are added to PendingAdds;
    /// traverse entries are added to PendingTraverseAdds. Refreshes grids when done.
    /// </summary>
    public void Import(Action refreshGrids)
    {
        using var openDialogAdapter = openFileDialogFactory.Create();
        var ofd = openDialogAdapter.Dialog;
        ofd.Title = "Import Grants";
        ofd.Filter = "RunFence Grants (*.rfg;*.json)|*.rfg;*.json|All files (*.*)|*.*";
        if (openDialogAdapter.ShowDialog(_owner) != DialogResult.OK)
            return;

        AclExportData exportData;
        try
        {
            var json = fileContentService.ReadAllText(ofd.FileName, Encoding.UTF8);
            exportData = JsonSerializer.Deserialize<AclExportData>(json, JsonOptions)
                         ?? throw new InvalidDataException("File is empty or not a valid grants export.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to read grants file '{ofd.FileName}'", ex);
            messageBoxService.Show(_owner, $"Import failed: {ex.Message}", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (exportData.Version != 1)
        {
            messageBoxService.Show(_owner, $"Unsupported grants file version: {exportData.Version}",
                "Import Grants", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AclImportResult importResult;
        try
        {
            importResult = importProcessor.ProcessImport(new AclImportRequest(exportData, _pending, _sid, _isContainer));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to process import data from '{ofd.FileName}'", ex);
            messageBoxService.Show(_owner, $"Import failed while processing entries: {ex.Message}", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (importResult.AnyAdded)
            refreshGrids();
        if (importResult.Warnings.Count > 0)
        {
            messageBoxService.Show(
                _owner,
                BuildImportWarningMessage(importResult.Warnings),
                "Import Grants",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!importResult.AnyAdded)
        {
            messageBoxService.Show(_owner, "No new entries to import (all paths already exist).", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }


    /// <summary>
    /// Iterates grant entries from the database (filtered by <paramref name="dbFilter"/>),
    /// applies <paramref name="selector"/> to produce export items, then appends pending adds
    /// transformed via the same <paramref name="selector"/>. Items where <paramref name="selector"/>
    /// returns null are skipped.
    /// </summary>
    private List<T> ScanPaths<T>(
        IEnumerable<GrantedPathEntry>? dbEntries,
        Func<GrantedPathEntry, bool> dbFilter,
        Func<GrantedPathEntry, T?> selector,
        IEnumerable<GrantedPathEntry> pendingEntries) where T : class
    {
        var result = new List<T>();

        if (dbEntries != null)
        {
            foreach (var entry in dbEntries.Where(dbFilter))
            {
                var item = selector(entry);
                if (item != null)
                    result.Add(item);
            }
        }

        result.AddRange(pendingEntries.Select(selector).OfType<T>());
        return result;
    }

    public AclExportData BuildFullExportData()
        => new(
            Version: 1,
            Grants: BuildAllGrants(),
            Traverse: _isContainer ? [] : BuildAllTraverse());

    public AclExportData BuildGrantSelectionExportData(IEnumerable<GrantedPathEntry> selectedEntries)
        => new(
            Version: 1,
            Grants: BuildGrantsFromEntries(selectedEntries),
            Traverse: []);

    public AclExportData BuildTraverseSelectionExportData(IEnumerable<GrantedPathEntry> selectedEntries)
        => new(
            Version: 1,
            Grants: [],
            Traverse: _isContainer ? [] : BuildTraverseFromEntries(selectedEntries));

    private List<AclExportGrantEntry> BuildAllGrants()
    {
        var dbGrants = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        return ScanPaths(
            dbGrants,
            dbFilter: e => !e.IsTraverseOnly &&
                           !_pending.Grants.IsPendingRemove(e.Path, e.IsDeny) &&
                           !_pending.Grants.IsUntrackGrant(e.Path, e.IsDeny),
            selector: GetExportRights,
            pendingEntries: _pending.Grants.GetPendingAddsSnapshot().Values);
    }

    private List<AclExportTraverseEntry> BuildAllTraverse()
    {
        var database = databaseProvider.GetDatabase();
        var dbGrants = database.GetAccount(GetTraverseLookupSid())?.Grants;

        return ScanPaths(
            dbGrants,
            dbFilter: e => e.IsTraverseOnly &&
                           traverseGrantOwnerResolver.EntryAppliesToSid(
                               e,
                               _sid,
                               includeManualSharedEntries: true) &&
                           !_pending.Traverse.IsPendingTraverseRemove(e.Path) &&
                           !_pending.Traverse.IsUntrackTraverse(e.Path),
            selector: e => new AclExportTraverseEntry(e.Path),
            pendingEntries: _pending.Traverse.GetPendingAddsSnapshot().Values);
    }

    private List<AclExportGrantEntry> BuildGrantsFromEntries(IEnumerable<GrantedPathEntry> entries)
    {
        var result = new List<AclExportGrantEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsTraverseOnly)
                continue;
            var rights = GetExportRights(entry);
            if (rights == null)
                continue;
            result.Add(rights);
        }

        return result;
    }

    private List<AclExportTraverseEntry> BuildTraverseFromEntries(IEnumerable<GrantedPathEntry> entries)
    {
        var result = new List<AclExportTraverseEntry>();
        foreach (var entry in entries)
        {
            if (!entry.IsTraverseOnly)
                continue;
            result.Add(new AclExportTraverseEntry(entry.Path));
        }

        return result;
    }

    private AclExportGrantEntry? GetExportRights(GrantedPathEntry entry)
    {
        var saved = entry.SavedRights;
        if (saved == null)
        {
            // Auto-populate from NTFS before export.
            try
            {
                var groupSids = aclPermission.ResolveAccountGroupSids(_sid);
                var state = grantInspectionService.ReadGrantState(entry.Path, _sid, groupSids);
                // Use comparer to build the saved rights using the same rules as auto-populate.
                _ = SavedRightsComparer.Instance.AutoPopulateMissingSavedRights(
                    [entry], _ => state, _isContainer);
                saved = entry.SavedRights;
                if (saved == null)
                {
                    log.Warn($"Export: could not read rights for '{entry.Path}' — skipping");
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.Warn($"Export: failed to read rights for '{entry.Path}': {ex.Message} — skipping");
                return null;
            }
        }

        return new AclExportGrantEntry(entry.Path, entry.IsDeny,
            saved.Execute, saved.Write, saved.Read, saved.Special, saved.Own);
    }

    private string GetTraverseLookupSid()
        => traverseGrantOwnerResolver.ResolveStorageOwnerSid(_sid);

    private static bool HasExportEntries(AclExportData exportData)
        => (exportData.Grants?.Count ?? 0) > 0 || (exportData.Traverse?.Count ?? 0) > 0;

    private static string BuildImportWarningMessage(IReadOnlyList<AclImportWarning> warnings)
    {
        var lines = warnings.Select(w => $"{w.Path}: {w.Message}");
        return "Some import entries were skipped:\n\n" + string.Join("\n\n", lines);
    }
}

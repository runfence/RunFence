using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles Export and Import of grant configurations for <see cref="AclManagerDialog"/>.
/// Export produces account-agnostic JSON (no SID). Import adds to pending state.
/// </summary>
public class AclManagerExportImport(
    IGrantedPathAclService aclService,
    ILoggingService log,
    IDatabaseProvider databaseProvider)
{
    private AclManagerPendingChanges _pending = null!;
    private string _sid = null!;
    private bool _isContainer;
    private IWin32Window _owner = null!;

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
        IWin32Window owner)
    {
        _pending = pending;
        _sid = sid;
        _isContainer = isContainer;
        _owner = owner;
    }

    /// <summary>
    /// Exports grants to a JSON file. The exported content depends on selection and active tab:
    /// nothing selected → export ALL; grants tab selection → selected grants only;
    /// traverse tab selection → selected traverse entries only.
    /// </summary>
    public void Export(DataGridView grantsGrid, DataGridView traverseGrid, bool grantsTabActive)
    {
        bool grantsSelected = grantsTabActive && grantsGrid.SelectedRows.Count > 0
                                              && grantsGrid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is GrantedPathEntry);
        bool traverseSelected = !grantsTabActive && traverseGrid.SelectedRows.Count > 0
                                                 && traverseGrid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is GrantedPathEntry);

        List<ExportGrantEntry> grantsToExport = [];
        List<ExportTraverseEntry> traverseToExport = [];

        if (grantsSelected)
        {
            grantsToExport = BuildGrantsFromSelection(grantsGrid);
        }
        else if (traverseSelected)
        {
            traverseToExport = BuildTraverseFromSelection(traverseGrid);
        }
        else
        {
            grantsToExport = BuildAllGrants();
            traverseToExport = BuildAllTraverse();
        }

        if (grantsToExport.Count == 0 && traverseToExport.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title = "Export Grants",
            Filter = "RunFence Grants (*.rfg)|*.rfg|JSON files (*.json)|*.json",
            DefaultExt = "rfg"
        };
        if (sfd.ShowDialog(_owner) != DialogResult.OK)
            return;

        try
        {
            var exportData = new ExportData(Version: 1, Grants: grantsToExport, Traverse: traverseToExport);
            var json = JsonSerializer.Serialize(exportData, JsonOptions);
            File.WriteAllText(sfd.FileName, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to export grants to '{sfd.FileName}'", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Export Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Imports grants from a JSON file. Grant entries are added to PendingAdds;
    /// traverse entries are added to PendingTraverseAdds. Refreshes grids when done.
    /// </summary>
    public void Import(Action refreshGrids)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Import Grants",
            Filter = "RunFence Grants (*.rfg;*.json)|*.rfg;*.json|All files (*.*)|*.*"
        };
        if (ofd.ShowDialog(_owner) != DialogResult.OK)
            return;

        ExportData exportData;
        try
        {
            var json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
            exportData = JsonSerializer.Deserialize<ExportData>(json, JsonOptions)
                         ?? throw new InvalidDataException("File is empty or not a valid grants export.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to read grants file '{ofd.FileName}'", ex);
            MessageBox.Show($"Import failed: {ex.Message}", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (exportData.Version != 1)
        {
            MessageBox.Show($"Unsupported grants file version: {exportData.Version}",
                "Import Grants", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool anyAdded = false;
        try
        {
            ProcessImport(exportData, () =>
            {
                anyAdded = true;
                refreshGrids();
            });
        }
        catch (Exception ex)
        {
            log.Error($"Failed to process import data from '{ofd.FileName}'", ex);
            MessageBox.Show($"Import failed while processing entries: {ex.Message}", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!anyAdded)
        {
            MessageBox.Show("No new entries to import (all paths already exist).", "Import Grants",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private bool IsTraverseAlreadyPresent(string normalizedPath) =>
        _pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), _sid, normalizedPath, checkUntrack: false);

    private List<ExportGrantEntry> BuildAllGrants()
    {
        var result = new List<ExportGrantEntry>();

        // DB grants (excluding pending removes).
        var dbGrants = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        if (dbGrants != null)
        {
            foreach (var entry in dbGrants.Where(e => !e.IsTraverseOnly &&
                                                      !_pending.IsPendingRemove(e.Path, e.IsDeny)))
            {
                var rights = GetExportRights(entry);
                if (rights == null)
                    continue;
                result.Add(rights);
            }
        }

        // Pending adds (not yet in DB).
        result.AddRange(_pending.PendingAdds.Values.Select(entry => GetExportRights(entry)).OfType<ExportGrantEntry>());

        return result;
    }

    private List<ExportTraverseEntry> BuildAllTraverse()
    {
        var result = new List<ExportTraverseEntry>();

        // DB traverse entries (excluding pending removes).
        var dbGrants = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        if (dbGrants != null)
        {
            foreach (var entry in dbGrants.Where(e => e.IsTraverseOnly &&
                                                      !_pending.IsPendingTraverseRemove(e.Path)))
                result.Add(new ExportTraverseEntry(entry.Path));
        }

        // Pending traverse adds (not yet in DB).
        result.AddRange(_pending.PendingTraverseAdds.Values.Select(entry => new ExportTraverseEntry(entry.Path)));

        return result;
    }

    private List<ExportGrantEntry> BuildGrantsFromSelection(DataGridView grantsGrid)
    {
        var result = new List<ExportGrantEntry>();
        foreach (DataGridViewRow row in grantsGrid.SelectedRows)
        {
            if (row.Tag is not GrantedPathEntry entry || entry.IsTraverseOnly)
                continue;
            var rights = GetExportRights(entry);
            if (rights == null)
                continue;
            result.Add(rights);
        }

        return result;
    }

    private List<ExportTraverseEntry> BuildTraverseFromSelection(DataGridView traverseGrid)
    {
        var result = new List<ExportTraverseEntry>();
        foreach (DataGridViewRow row in traverseGrid.SelectedRows)
        {
            if (row.Tag is not GrantedPathEntry entry || !entry.IsTraverseOnly)
                continue;
            result.Add(new ExportTraverseEntry(entry.Path));
        }

        return result;
    }

    private ExportGrantEntry? GetExportRights(GrantedPathEntry entry)
    {
        var saved = entry.SavedRights;
        if (saved == null)
        {
            // Auto-populate from NTFS before export.
            try
            {
                var groupSids = new List<string>(); // group SIDs not needed for export population
                var state = aclService.ReadRights(entry.Path, _sid, groupSids);
                // Use comparer to build the saved rights using the same rules as auto-populate.
                var populated = SavedRightsComparer.Instance.AutoPopulateMissingSavedRights(
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

        return new ExportGrantEntry(entry.Path, entry.IsDeny,
            saved.Execute, saved.Write, saved.Read, saved.Special, saved.Own);
    }

    // --- Export/Import data model (account-agnostic, no SID) ---

    public record ExportData(
        [property: JsonPropertyName("version")]
        int Version,
        [property: JsonPropertyName("grants")] List<ExportGrantEntry>? Grants,
        [property: JsonPropertyName("traverse")]
        List<ExportTraverseEntry>? Traverse);

    public record ExportGrantEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("isDeny")] bool IsDeny,
        [property: JsonPropertyName("execute")]
        bool Execute,
        [property: JsonPropertyName("write")] bool Write,
        [property: JsonPropertyName("read")] bool Read,
        [property: JsonPropertyName("special")]
        bool Special,
        [property: JsonPropertyName("owner")] bool Owner);

    public record ExportTraverseEntry(
        [property: JsonPropertyName("path")] string Path);

    /// <summary>
    /// Processes import from already-deserialized data. Used by unit tests to bypass file dialog.
    /// </summary>
    public void ProcessImport(ExportData exportData, Action refreshGrids)
    {
        if (exportData.Version != 1)
            return;

        int grantsAdded = 0;
        int traverseAdded = 0;
        var addedGrantKeys = new List<(string Path, bool IsDeny)>();
        var addedTraverseKeys = new List<string>();

        try
        {
            foreach (var g in exportData.Grants ?? [])
            {
                if (string.IsNullOrEmpty(g.Path))
                    continue;
                var normalized = Path.GetFullPath(g.Path);

                bool inDb = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants
                    .Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)
                              && e.IsDeny == g.IsDeny && !e.IsTraverseOnly) == true;
                if (inDb)
                {
                    // If pending removal, cancel the removal (import restores the entry).
                    _pending.PendingRemoves.Remove((normalized, g.IsDeny));
                    continue;
                }

                if (_pending.IsPendingAdd(normalized, g.IsDeny))
                    continue;

                // Build from mode defaults and override only the configurable bits, enforcing always-on
                // bits (Deny: Write+Special always on; Allow: Read always on) regardless of import data.
                var savedRights = g.IsDeny
                    ? SavedRightsState.DefaultForMode(true, own: g.Owner) with { Execute = g.Execute, Read = g.Read }
                    : SavedRightsState.DefaultForMode(false, own: g.Owner) with { Execute = g.Execute, Write = g.Write, Special = g.Special };
                var entry = new GrantedPathEntry { Path = normalized, IsDeny = g.IsDeny, SavedRights = savedRights };
                _pending.PendingAdds[(normalized, g.IsDeny)] = entry;
                addedGrantKeys.Add((normalized, g.IsDeny));
                grantsAdded++;
            }

            var traverseInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in exportData.Traverse ?? [])
                if (!string.IsNullOrEmpty(t.Path))
                    traverseInFile.Add(Path.GetFullPath(t.Path));

            foreach (var t in exportData.Traverse ?? [])
            {
                if (string.IsNullOrEmpty(t.Path))
                    continue;
                var normalized = Path.GetFullPath(t.Path);
                if (IsTraverseAlreadyPresent(normalized))
                    continue;

                var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
                _pending.PendingTraverseAdds[normalized] = entry;
                addedTraverseKeys.Add(normalized);
                traverseAdded++;
            }

            foreach (var g in exportData.Grants ?? [])
            {
                if (g.IsDeny || string.IsNullOrEmpty(g.Path))
                    continue;
                var normalizedGrant = Path.GetFullPath(g.Path);
                var traversePath = TraverseAutoManager.GetTraversePath(normalizedGrant);
                if (traversePath == null)
                    continue;

                if (traverseInFile.Contains(traversePath))
                    continue;
                if (IsTraverseAlreadyPresent(traversePath))
                    continue;

                var entry = new GrantedPathEntry { Path = traversePath, IsTraverseOnly = true };
                _pending.PendingTraverseAdds[traversePath] = entry;
                addedTraverseKeys.Add(traversePath);
                traverseAdded++;
            }
        }
        catch
        {
            foreach (var key in addedGrantKeys)
                _pending.PendingAdds.Remove(key);
            foreach (var key in addedTraverseKeys)
                _pending.PendingTraverseAdds.Remove(key);
            throw;
        }

        if (grantsAdded > 0 || traverseAdded > 0)
            refreshGrids();
    }
}
using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles traverse-tab grid population, add/remove path operations, and tab-switching
/// button state for <see cref="AclManagerDialog"/>.
/// </summary>
public class AclManagerTraverseHelper(
    IAppConfigService appConfigService,
    IAclPermissionService aclPermission,
    IAppContainerService containerService,
    IUserTraverseService userTraverseService,
    IGrantConfigTracker grantConfigTracker,
    IDatabaseProvider databaseProvider)
{
    private DataGridView _traverseGrid = null!;
    private string _sid = null!;
    private Font? _boldFont;
    private readonly Dictionary<GrantedPathEntry, GrantedPathEntry> _syntheticEntries = new();
    private TraverseEntryResolver _resolver = null!;
    private AclManagerPendingChanges _pending = null!;
    private GridSortHelper? _sortHelper;
    private Lazy<IReadOnlyList<string>> _groupSids = null!;
    private bool _isContainer;

    public void Initialize(
        DataGridView traverseGrid,
        string sid,
        AclManagerPendingChanges pending,
        bool isContainer,
        GridSortHelper? sortHelper = null)
    {
        _traverseGrid = traverseGrid;
        _sid = sid;
        _pending = pending;
        _isContainer = isContainer;
        _sortHelper = sortHelper;
        _resolver = new TraverseEntryResolver(sid, databaseProvider, isContainer, containerService,
            userTraverseService, aclPermission, _syntheticEntries, grantConfigTracker);
        _groupSids = new Lazy<IReadOnlyList<string>>(() => aclPermission.ResolveAccountGroupSids(sid));
    }

    public HashSet<GrantedPathEntry> FixableEntries { get; } = new();

    public void DisposeBoldFont()
    {
        _boldFont?.Dispose();
        _boldFont = null;
    }

    public void PopulateTraverseGrid()
    {
        _traverseGrid.Rows.Clear();
        FixableEntries.Clear();
        _syntheticEntries.Clear();
        bool hasLoadedConfigs = appConfigService.HasLoadedConfigs;

        List<GrantedPathEntry> traverseEntries;
        var dbGrants = databaseProvider.GetDatabase().GetAccount(_sid)?.Grants;
        if (dbGrants == null)
        {
            if (!hasLoadedConfigs && _pending.PendingTraverseAdds.Count == 0)
                return;
            traverseEntries = [];
        }
        else
        {
            // Exclude entries that are pending removal or untrack — they will be gone after Apply.
            traverseEntries = dbGrants
                .Where(e => e.IsTraverseOnly &&
                            !_pending.IsPendingTraverseRemove(Path.GetFullPath(e.Path)) &&
                            !_pending.IsUntrackTraverse(Path.GetFullPath(e.Path)))
                .ToList();
            if (traverseEntries.Count == 0 && !hasLoadedConfigs && _pending.PendingTraverseAdds.Count == 0)
                return;
        }

        // Merge pending traverse adds (those not already represented in DB entries).
        var pendingNewAdds = _pending.PendingTraverseAdds.Values
            .Where(e => !traverseEntries.Any(existing =>
                string.Equals(existing.Path, e.Path, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var allEntriesToShow = traverseEntries.Concat(pendingNewAdds).ToList();

        var mainEntries = allEntriesToShow
            .Where(e => GetEffectiveTraverseConfigPath(e) == null)
            .ToList();
        bool hasPendingForMain = pendingNewAdds.Any(e => GetEffectiveTraverseConfigPath(e) == null);
        AddTraverseRows(mainEntries, "Main Config", configPath: null,
            showIfEmpty: hasLoadedConfigs || hasPendingForMain);

        foreach (var configPath in appConfigService.GetLoadedConfigPaths())
        {
            var configEntries = allEntriesToShow
                .Where(e => string.Equals(
                    GetEffectiveTraverseConfigPath(e), configPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            AddTraverseRows(configEntries, Path.GetFileName(configPath), configPath, showIfEmpty: true);
        }

        _traverseGrid.ClearSelection();
    }

    /// <summary>
    /// Returns the effective config path for a traverse entry, accounting for any pending
    /// config-section move that hasn't been applied yet.
    /// </summary>
    private string? GetEffectiveTraverseConfigPath(GrantedPathEntry e)
    {
        var path = Path.GetFullPath(e.Path);
        if (_pending.PendingTraverseConfigMoves.TryGetValue(path, out var pendingTarget))
            return pendingTarget;
        return grantConfigTracker.GetGrantConfigPath(_sid, e);
    }

    private void AddTraverseRows(List<GrantedPathEntry> entries, string sectionTitle, string? configPath, bool showIfEmpty = false)
    {
        if (!showIfEmpty && entries.Count == 0)
            return;

        // Sort within section when a column sort is active.
        if (_sortHelper?.IsSortActive == true)
            entries = _sortHelper.SortByActiveColumn(entries, e => e.Path).ToList();

        var headerRow = AclManagerSectionHeader.CreateSectionHeaderRow(_traverseGrid, sectionTitle, configPath, titleCellIndex: 1);
        _traverseGrid.Rows.Add(headerRow);

        foreach (var entry in entries)
            AddTraverseEntryRow(entry);
    }

    private void AddTraverseEntryRow(GrantedPathEntry entry)
    {
        var sidIdentity = new SecurityIdentifier(_sid);
        var groupSids = _groupSids.Value;

        if (entry.AllAppliedPaths is { Count: > 0 })
        {
            if (!PathExists(entry.Path))
            {
                AddSingleTraverseRow(entry, isGray: true);
                var (synthetic, allEffective) = _resolver.CreateNearestAncestorEntry(entry, sidIdentity, groupSids);
                if (synthetic != null)
                {
                    if (synthetic.AllAppliedPaths is { Count: > 0 })
                        AddSingleTraverseRow(synthetic, isGray: false, isYellow: !allEffective);
                    else
                        AddSingleTraverseRow(synthetic, isGray: false);
                }
            }
            else
            {
                // Yellow only if any path in AllAppliedPaths lacks effective traverse
                // (explicit + inherited + group membership) — not just an explicit ACE.
                bool allEffective = entry.AllAppliedPaths.All(p => _resolver.HasEffectiveTraverse(p, sidIdentity, groupSids));
                AddSingleTraverseRow(entry, isGray: false, isYellow: !allEffective);
            }
        }
        else
        {
            if (!PathExists(entry.Path))
            {
                AddSingleTraverseRow(entry, isGray: true);
            }
            else
            {
                // No AllAppliedPaths recorded (old entry) — walk ancestor chain for display.
                // Populates entry.AllAppliedPaths so bold/yellow checks cover the full path
                // chain rather than just entry.Path, and upgrades the entry to tracked format.
                var ancestorPaths = new List<string>();
                for (var dir = new DirectoryInfo(entry.Path); dir != null; dir = dir.Parent)
                {
                    if (dir.Exists)
                        ancestorPaths.Add(dir.FullName);
                }

                if (ancestorPaths.Count > 0)
                    entry.AllAppliedPaths = ancestorPaths;

                bool allEffective = ancestorPaths.Count > 0
                    ? ancestorPaths.All(p => _resolver.HasEffectiveTraverse(p, sidIdentity, groupSids))
                    : _resolver.HasEffectiveTraverse(entry.Path, sidIdentity, groupSids);
                AddSingleTraverseRow(entry, isGray: false, isYellow: !allEffective);
            }
        }
    }

    private void AddSingleTraverseRow(GrantedPathEntry entry, bool isGray, bool isYellow = false)
    {
        var row = new DataGridViewRow();
        row.CreateCells(_traverseGrid);
        row.Tag = entry;

        row.Cells[_traverseGrid.Columns[AclManagerGrantsHelper.ColIcon]!.Index].Value =
            AclPathIconProvider.GetIcon(entry.Path);
        row.Cells[_traverseGrid.Columns["TraversePath"]!.Index].Value = entry.Path;

        var normalizedPath = Path.GetFullPath(entry.Path);
        bool isPendingGreen = _pending.IsPendingTraverseAdd(normalizedPath) ||
                              _pending.PendingTraverseFixes.ContainsKey(normalizedPath) ||
                              _pending.IsPendingTraverseConfigMove(normalizedPath);

        if (isPendingGreen)
        {
            // Green takes priority over yellow — intent is recorded, will be applied.
            AclManagerGrantRowRenderer.SetPendingRowColor(row);
        }
        else if (isGray)
        {
            row.DefaultCellStyle.ForeColor = Color.Gray;
            row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
        }
        else if (isYellow)
        {
            row.DefaultCellStyle.BackColor = Color.LightYellow;
            FixableEntries.Add(entry);
        }

        if (entry.AllAppliedPaths is { Count: > 0 })
            row.DefaultCellStyle.Font = _boldFont ??= new Font(_traverseGrid.Font, FontStyle.Bold);
        _traverseGrid.Rows.Add(row);
    }

    /// <summary>
    /// Adds a traverse path entry. Shows file/folder dialogs, validates, records to pending
    /// (no NTFS or DB write), and refreshes the grid.
    /// Returns an error message string when the path is already in the list; null on success or cancel.
    /// </summary>
    public string? AddTraversePath(bool isFolder, IWin32Window owner)
    {
        string? selectedPath;
        if (isFolder)
        {
            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select folder to grant traverse access";
            fbd.UseDescriptionForTitle = true;
            if (fbd.ShowDialog(owner) != DialogResult.OK)
                return null;
            selectedPath = fbd.SelectedPath;
        }
        else
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Select file to grant traverse access";
            ofd.Filter = "All files (*.*)|*.*";
            if (ofd.ShowDialog(owner) != DialogResult.OK)
                return null;
            selectedPath = ofd.FileName;
        }

        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var pathsToAdd = ReparsePointPromptHelper.ResolveForAdd(selectedPath, owner);
        return pathsToAdd.Aggregate<string, string?>(null, (current, p) => AddTraversePathDirect(p) ?? current);
    }

    /// <summary>
    /// Adds a traverse path entry for a known path (e.g. from shell drag-drop). Validates,
    /// records to pending (no NTFS or DB write), and refreshes the grid.
    /// Returns an error message string when the path is already in the list; null on success.
    /// </summary>
    public string? AddTraversePathDirect(string selectedPath)
    {
        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var normalized = Path.GetFullPath(selectedPath);

        // Check for duplicate in DB or pending adds (excluding pending removes/untracks — those are being removed).
        if (_pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), _sid, normalized))
            return "This path is already in the list.";

        // If this path was previously queued for removal or untrack, cancel it instead.
        if (_pending.PendingTraverseRemoves.Remove(normalized) ||
            _pending.PendingUntrackTraverse.Remove(normalized))
        {
            PopulateTraverseGrid();
            return null;
        }

        var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
        _pending.PendingTraverseAdds[normalized] = entry;
        PopulateTraverseGrid();
        return null;
    }

    /// <summary>
    /// Removes one or more traverse path entries (deferred). If the entry was a pending add,
    /// it is simply discarded. Otherwise the entry is added to <see cref="AclManagerPendingChanges.PendingTraverseRemoves"/>
    /// and removed from the grid immediately. No NTFS or DB writes occur until Apply.
    /// </summary>
    public void RemoveTraversePaths(IReadOnlyList<GrantedPathEntry> entries)
    {
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
        {
            var normalized = Path.GetFullPath(entry.Path);
            if (!_pending.PendingTraverseAdds.Remove(normalized))
                // Existing DB entry — queue for deferred removal.
                _pending.PendingTraverseRemoves[normalized] = entry;
            _pending.PendingTraverseConfigMoves.Remove(normalized);
        }

        PopulateTraverseGrid();
    }

    /// <summary>
    /// Records traverse path entries as pending fix (deferred). The rows are shown in green
    /// to indicate the fix intent is recorded and will be applied. No NTFS writes occur until Apply.
    /// </summary>
    public void FixTraversePaths(IReadOnlyList<GrantedPathEntry> entries)
    {
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
            _pending.PendingTraverseFixes[Path.GetFullPath(entry.Path)] = entry;
        PopulateTraverseGrid();
    }

    /// <summary>
    /// Ensures a traverse entry is pending for <paramref name="grantDir"/> for the current SID.
    /// Called automatically when an allow grant is added in the grants tab.
    /// Adds to <see cref="AclManagerPendingChanges.PendingTraverseAdds"/> if no traverse entry
    /// already exists in DB or pending. Cancels a pending removal if one exists for the same path.
    /// No NTFS writes occur until Apply.
    /// Returns true if the traverse grid should be repopulated (entry added or removal cancelled).
    /// </summary>
    public bool TryEnsureTraverseAccess(string grantDir)
    {
        var normalized = Path.GetFullPath(grantDir);

        // Already covered by a DB entry or pending add (not pending removal or untrack)?
        if (_pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), _sid, normalized))
            return false;

        // If this was queued for removal, cancel the removal (re-activate it).
        if (_pending.PendingTraverseRemoves.Remove(normalized))
            return true;

        // Skip add when the SID already has effective traverse rights on this path.
        if (TraverseRightsHelper.HasEffectiveTraverse(normalized, _sid, _groupSids.Value, aclPermission))
            return false;

        var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
        _pending.PendingTraverseAdds[normalized] = entry;
        return true;
    }

    /// <summary>
    /// Immediately applies traverse ACEs to NTFS for a traverse entry.
    /// Called from Apply pipeline on a background thread.
    /// Returns the list of paths that had ACEs applied (for the caller to set
    /// <see cref="GrantedPathEntry.AllAppliedPaths"/>), or an empty list if no paths were visited.
    /// </summary>
    public List<string> ApplyTraverseEntryNow(GrantedPathEntry entry)
    {
        if (_isContainer)
        {
            var database = databaseProvider.GetDatabase();
            var container = database.AppContainers.FirstOrDefault(c =>
                string.Equals(containerService.GetSid(c.Name), _sid, StringComparison.OrdinalIgnoreCase));
            if (container != null)
            {
                var (_, appliedPaths) = containerService.EnsureTraverseAccess(container, entry.Path);
                return appliedPaths;
            }

            return [];
        }

        var (_, visitedPaths) = userTraverseService.EnsureTraverseAccess(_sid, entry.Path);
        return visitedPaths;
    }

    /// <summary>
    /// Re-applies traverse ACEs for an existing (Fix) entry and updates
    /// <see cref="GrantedPathEntry.AllAppliedPaths"/> with the freshly visited paths,
    /// removing any stale or deleted entries from tracking.
    /// Called from Apply pipeline on a background thread.
    /// </summary>
    public void FixTraverseEntryNow(GrantedPathEntry entry)
    {
        if (_isContainer)
        {
            var database = databaseProvider.GetDatabase();
            var container = database.AppContainers.FirstOrDefault(c =>
                string.Equals(containerService.GetSid(c.Name), _sid, StringComparison.OrdinalIgnoreCase));
            if (container != null)
            {
                var (_, appliedPaths) = containerService.EnsureTraverseAccess(container, entry.Path);
                if (appliedPaths.Count > 0)
                    entry.AllAppliedPaths = appliedPaths;
            }
        }
        else
        {
            var (_, visitedPaths) = userTraverseService.EnsureTraverseAccess(_sid, entry.Path);
            if (visitedPaths.Count > 0)
                entry.AllAppliedPaths = visitedPaths;
        }
    }

    /// <summary>
    /// Immediately reverts traverse ACEs from NTFS for a traverse entry.
    /// Called from Apply pipeline on a background thread.
    /// Delegates to <see cref="TraverseEntryResolver"/> for the underlying operation.
    /// </summary>
    public void RevertTraverseEntry(GrantedPathEntry entry)
    {
        _resolver.RevertTraverseAces(entry);
    }

    private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);
}
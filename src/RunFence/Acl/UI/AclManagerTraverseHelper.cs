using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles traverse-tab grid population and tab-switching button state for
/// <see cref="AclManagerDialog"/>. Mutation operations (add/remove/fix/ensure) are
/// delegated to <see cref="AclManagerTraverseOperations"/>.
/// </summary>
public class AclManagerTraverseHelper(
    IAppConfigService appConfigService,
    IAclPermissionService aclPermission,
    IGrantIntentRepository grantIntentRepository,
    IGrantIntentStoreProvider grantIntentStoreProvider,
    IDatabaseProvider databaseProvider,
    ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
    AclManagerTraverseOperations traverseOperations,
    AclManagerTraverseRowBuilder rowBuilder)
{
    private DataGridView _traverseGrid = null!;
    private string _sid = null!;
    private bool _isContainer;
    private AclManagerPendingChanges _pending = null!;
    private GridSortHelper? _sortHelper;
    private Lazy<IReadOnlyList<string>> _groupSids = null!;

    public void Initialize(
        DataGridView traverseGrid,
        string sid,
        bool isContainer,
        AclManagerPendingChanges pending,
        GridSortHelper? sortHelper = null)
    {
        _traverseGrid = traverseGrid;
        _sid = sid;
        _isContainer = isContainer;
        _pending = pending;
        _sortHelper = sortHelper;
        _groupSids = new Lazy<IReadOnlyList<string>>(() => aclPermission.ResolveAccountGroupSids(sid));
        rowBuilder.Initialize(traverseGrid, sid, pending, _groupSids);
        traverseOperations.Initialize(sid, pending, _groupSids, PopulateTraverseGrid);
    }

    public HashSet<GrantedPathEntry> FixableEntries => rowBuilder.FixableEntries;

    public void DisposeBoldFont() => rowBuilder.DisposeBoldFont();

    public void PopulateTraverseGrid()
    {
        _traverseGrid.Rows.Clear();
        rowBuilder.ClearFixableEntries();
        bool hasLoadedConfigs = appConfigService.HasLoadedConfigs;

        List<GrantedPathEntry> traverseEntries;
        var database = databaseProvider.GetDatabase();
        var dbGrants = GetDbTraverseEntries(database);
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
            .Where(e => GetEffectiveConfigPath(e) == null)
            .ToList();
        bool hasPendingForMain = pendingNewAdds.Any(e => GetEffectiveConfigPath(e) == null);
        AddTraverseRows(mainEntries, "Main Config", configPath: null,
            showIfEmpty: hasLoadedConfigs || hasPendingForMain);

        foreach (var configPath in appConfigService.GetLoadedConfigPaths())
        {
            var configEntries = allEntriesToShow
                .Where(e => string.Equals(
                    GetEffectiveConfigPath(e), configPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            AddTraverseRows(configEntries, Path.GetFileName(configPath), configPath, showIfEmpty: true);
        }

        _traverseGrid.ClearSelection();
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
        if (entry.AllAppliedPaths is { Count: > 0 })
            rowBuilder.AddTrackedTraverseRow(entry);
        else
            rowBuilder.AddLegacyTraverseRow(entry);
    }

    private List<GrantedPathEntry>? GetDbTraverseEntries(AppDatabase database)
    {
        return database.GetAccount(GetTraverseLookupSid())?.Grants
            .Where(entry => traverseGrantOwnerResolver.EntryAppliesToSid(
                entry,
                _sid,
                includeManualSharedEntries: true))
            .ToList();
    }

    private string GetTraverseLookupSid()
        => traverseGrantOwnerResolver.ResolveStorageOwnerSid(_sid);

    private string? GetEffectiveConfigPath(GrantedPathEntry entry)
    {
        var normalizedPath = Path.GetFullPath(entry.Path);
        if (_pending.PendingTraverseConfigMoves.TryGetValue(normalizedPath, out var pendingTarget))
            return NormalizeConfigPath(pendingTarget.TargetConfigPath);

        var lookupSid = GetTraverseLookupSid();
        return grantIntentRepository.FindTraverse(lookupSid, entry)?.Store.ConfigPath;
    }

    private string? NormalizeConfigPath(string? configPath)
        => grantIntentStoreProvider.ResolveStore(configPath).ConfigPath;
}

using RunFence.Account;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles grid population, row rendering, and selection for <see cref="ApplicationsPanel"/>.
/// Extracts all grid-building methods from the panel to reduce its size and separate
/// data presentation from event handling.
/// </summary>
public class ApplicationsGridPopulator(
    IIconService iconService,
    IAppConfigService appConfigService,
    ISidNameCacheService sidNameCache)
{
    // Shared fallback icon used when no app icon is available. Avoids allocating a new Bitmap
    // per row (which would leak GDI handles since DataGridView does not dispose cell images).
    private static readonly Bitmap FallbackIcon = new(16, 16);

    private DataGridView _grid = null!;
    private IApplicationsPanelState _state = null!;
    private Func<IEnumerable<AppEntry>, Func<AppEntry, string>, IOrderedEnumerable<AppEntry>> _sortByActiveColumn = null!;

    private Font? _groupHeaderFont;
    private readonly Dictionary<string, (Image Icon, string ExePath)> _iconCache = new(StringComparer.Ordinal);

    public void Initialize(DataGridView grid, IApplicationsPanelState state,
        Func<IEnumerable<AppEntry>, Func<AppEntry, string>, IOrderedEnumerable<AppEntry>> sortByActiveColumn)
    {
        _grid = grid;
        _state = state;
        _sortByActiveColumn = sortByActiveColumn;
    }

    /// <summary>
    /// Rebuilds all grid rows from the current database state.
    /// </summary>
    public void PopulateGrid(AppGridDragDropHandler dragDropHandler,
        Action<bool> setIsRefreshing, Action reapplyGlyphIfActive)
    {
        string? selectedAppId = null;
        if (_grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is AppEntry prevApp)
            selectedAppId = prevApp.Id;

        dragDropHandler.ClearDropTargetOnRowsClear();
        setIsRefreshing(true);
        DisposeNonCachedIcons();
        _grid.Rows.Clear();

        bool hasAdditionalConfigs = appConfigService.HasLoadedConfigs;
        var database = _state.Database;

        var mainApps = SortApps(database.Apps
            .Where(a => appConfigService.GetConfigPath(a.Id) == null));

        var additionalGroups = appConfigService.GetLoadedConfigPaths()
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                Path = p,
                Apps = SortApps(database.Apps
                    .Where(a => string.Equals(appConfigService.GetConfigPath(a.Id), p,
                        StringComparison.OrdinalIgnoreCase)))
            })
            .ToList();

        if (hasAdditionalConfigs)
            AddGroupHeader("Main Config", null);
        foreach (var app in mainApps)
            AddAppRow(app);

        foreach (var group in additionalGroups)
        {
            if (hasAdditionalConfigs)
                AddGroupHeader(Path.GetFileName(group.Path), group.Path);
            foreach (var app in group.Apps)
                AddAppRow(app);
        }

        PruneStaleCacheEntries(database.Apps);

        setIsRefreshing(false);
        reapplyGlyphIfActive();
        if (selectedAppId != null)
            SelectAppById(selectedAppId);
        else
            SelectFirstRow();
    }

    /// <summary>
    /// Resolves the display name for an app entry's account using the canonical path:
    /// live OS lookup -> registry profile -> SidNames map -> raw SID.
    /// Applies credential-type labels (current / interactive). Returns the container name for AppContainer apps.
    /// </summary>
    public string ResolveAppAccountName(AppEntry app)
    {
        if (app.AppContainerName != null)
            return app.AppContainerName;

        var credentialStore = _state.CredentialStore;
        var cred = credentialStore.Credentials.FirstOrDefault(c =>
            string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));

        var displayName = sidNameCache.GetDisplayName(app.AccountSid);
        if (cred != null)
        {
            return cred.IsCurrentAccount ? $"{displayName} (current)"
                : cred.IsInteractiveUser ? $"{displayName} (interactive)"
                : displayName;
        }

        return displayName;
    }

    public void SelectAppById(string? appId)
    {
        if (appId == null)
            return;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is AppEntry app && app.Id == appId)
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Name"];
                return;
            }
        }
    }

    public void SelectFirstRow()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is AppEntry)
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Name"];
                return;
            }
        }
    }

    public void SelectRowByIndex(int index)
    {
        if (_grid.Rows.Count == 0)
            return;
        var target = Math.Min(index, _grid.Rows.Count - 1);
        for (int i = target; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].Tag is AppEntry)
            {
                _grid.Rows[i].Selected = true;
                _grid.CurrentCell = _grid.Rows[i].Cells["Name"];
                return;
            }
        }

        SelectFirstRow();
    }

    public void DisposeFont()
    {
        _groupHeaderFont?.Dispose();
        _groupHeaderFont = null;
        DisposeCachedIcons();
    }

    private Image GetOrCacheIcon(AppEntry app)
    {
        if (_iconCache.TryGetValue(app.Id, out var cached) &&
            string.Equals(cached.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase))
            return cached.Icon;

        // Dispose the previous cached icon for this app (ExePath changed or first load).
        if (cached.Icon is not null)
        {
            cached.Icon.Dispose();
            _iconCache.Remove(app.Id);
        }

        var icon = iconService.GetOriginalAppIcon(app) ?? FallbackIcon;
        if (icon != FallbackIcon)
            _iconCache[app.Id] = (icon, app.ExePath);
        return icon;
    }

    /// <summary>
    /// Disposes row icon images that are not in the cache and not the shared fallback.
    /// Called before clearing rows to prevent GDI handle leaks.
    /// </summary>
    private void DisposeNonCachedIcons()
    {
        var cachedImages = new HashSet<Image>(_iconCache.Values.Select(e => e.Icon)) { FallbackIcon };
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells[0].Value is Image img && !cachedImages.Contains(img))
                img.Dispose();
        }
    }

    private void DisposeCachedIcons()
    {
        foreach (var entry in _iconCache.Values)
            entry.Icon.Dispose();
        _iconCache.Clear();
    }

    private void PruneStaleCacheEntries(IReadOnlyList<AppEntry> currentApps)
    {
        var currentIds = currentApps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var staleIds = _iconCache.Keys.Where(id => !currentIds.Contains(id)).ToList();
        foreach (var id in staleIds)
        {
            _iconCache[id].Icon.Dispose();
            _iconCache.Remove(id);
        }
    }

    private IEnumerable<AppEntry> SortApps(IEnumerable<AppEntry> apps)
    {
        if (!_state.IsSortActive)
            return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

        // Columns: 0=Icon, 1=Name, 2=ExePath, 3=Account, 4=ACL, 5=Shortcuts
        Func<AppEntry, string> key = _state.SortColumnIndex switch
        {
            2 => a => a.ExePath,
            3 => a => a.AccountSid,
            4 => a => a.RestrictAcl ? a.AclMode == AclMode.Allow ? "A" : "D" : "",
            5 => a => a.ManageShortcuts ? "1" : "0",
            _ => a => a.Name
        };
        return _sortByActiveColumn(apps, key);
    }

    private void AddAppRow(AppEntry app)
    {
        var accountName = ResolveAppAccountName(app);
        var aclInfo = app.IsUrlScheme ? "N/A"
            : !app.RestrictAcl ? "Off"
            : app.AclMode == AclMode.Allow ? $"Allow/{app.AclTarget}"
            : $"Deny/{app.AclTarget}";
        var shortcutInfo = app.ManageShortcuts ? "Yes" : "No";

        Image icon = GetOrCacheIcon(app);
        var idx = _grid.Rows.Add(icon, app.Name, app.ExePath, accountName, aclInfo, shortcutInfo);
        _grid.Rows[idx].Tag = app;
    }

    private void AddGroupHeader(string label, string? configPath)
    {
        _groupHeaderFont ??= new Font(_grid.Font, FontStyle.Bold);
        var headerIdx = _grid.Rows.Add(FallbackIcon, label, "", "", "", "");
        var hr = _grid.Rows[headerIdx];
        hr.Tag = new ApplicationsPanel.ConfigGroupHeaderTag(configPath);
        hr.DefaultCellStyle.BackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        hr.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xD0, 0xD8, 0xEC);
        hr.DefaultCellStyle.Font = _groupHeaderFont;
        hr.DefaultCellStyle.ForeColor = Color.FromArgb(0x22, 0x22, 0x66);
    }
}
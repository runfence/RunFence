using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles grid population, row rendering, and selection for <see cref="ApplicationsPanel"/>.
/// Extracts all grid-building methods from the panel to reduce its size and separate
/// data presentation from event handling.
/// </summary>
public class ApplicationsGridPopulator
{
    private readonly IIconService _iconService;
    private readonly IAppConfigService _appConfigService;
    private readonly ISidResolver _sidResolver;
    private DataGridView _grid = null!;
    private IApplicationsPanelState _state = null!;
    private Func<IEnumerable<AppEntry>, Func<AppEntry, string>, IOrderedEnumerable<AppEntry>> _sortByActiveColumn = null!;

    private Font? _groupHeaderFont;

    public ApplicationsGridPopulator(
        IIconService iconService,
        IAppConfigService appConfigService,
        ISidResolver sidResolver)
    {
        _iconService = iconService;
        _appConfigService = appConfigService;
        _sidResolver = sidResolver;
    }

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
        _grid.Rows.Clear();

        bool hasAdditionalConfigs = _appConfigService.HasLoadedConfigs;
        var database = _state.Database;

        var mainApps = SortApps(database.Apps
            .Where(a => _appConfigService.GetConfigPath(a.Id) == null));

        var additionalGroups = _appConfigService.GetLoadedConfigPaths()
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                Path = p,
                Apps = SortApps(database.Apps
                    .Where(a => string.Equals(_appConfigService.GetConfigPath(a.Id), p,
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

        setIsRefreshing(false);
        reapplyGlyphIfActive();
        if (selectedAppId != null)
            SelectAppById(selectedAppId);
        else
            SelectFirstRow();
    }

    /// <summary>
    /// Resolves the display name for an app entry's account using the canonical path:
    /// SidNameCache (live OS lookup) -> registry profile -> SidNames map -> raw SID.
    /// Applies credential-type labels (current / interactive). Returns the container name for AppContainer apps.
    /// </summary>
    public string ResolveAppAccountName(AppEntry app)
    {
        if (app.AppContainerName != null)
            return app.AppContainerName;

        var credentialStore = _state.CredentialStore;
        var cred = credentialStore.Credentials.FirstOrDefault(c =>
            string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));

        var session = _state.Session;
        if (!session.SidNameCache.TryGetValue(app.AccountSid, out var cachedResolved))
        {
            cachedResolved = _sidResolver.TryResolveName(app.AccountSid);
            session.SidNameCache[app.AccountSid] = cachedResolved;
        }

        var database = _state.Database;
        if (cred != null)
        {
            if (cachedResolved != null)
            {
                var username = SidNameResolver.ExtractUsername(cachedResolved);
                return cred.IsCurrentAccount ? $"{username} (current)"
                    : cred.IsInteractiveUser ? $"{username} (interactive)"
                    : username;
            }

            return SidNameResolver.GetDisplayName(cred, _sidResolver, database.SidNames);
        }

        return SidNameResolver.GetDisplayName(app.AccountSid, cachedResolved, _sidResolver, database.SidNames);
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

        Image icon = _iconService.GetOriginalAppIcon(app) ?? new Bitmap(16, 16);
        var idx = _grid.Rows.Add(icon, app.Name, app.ExePath, accountName, aclInfo, shortcutInfo);
        _grid.Rows[idx].Tag = app;
    }

    private void AddGroupHeader(string label, string? configPath)
    {
        _groupHeaderFont ??= new Font(_grid.Font, FontStyle.Bold);
        var headerIdx = _grid.Rows.Add(new Bitmap(16, 16), label, "", "", "", "");
        var hr = _grid.Rows[headerIdx];
        hr.Tag = new ApplicationsPanel.ConfigGroupHeaderTag(configPath);
        hr.DefaultCellStyle.BackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        hr.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xD0, 0xD8, 0xEC);
        hr.DefaultCellStyle.Font = _groupHeaderFont;
        hr.DefaultCellStyle.ForeColor = Color.FromArgb(0x22, 0x22, 0x66);
    }
}
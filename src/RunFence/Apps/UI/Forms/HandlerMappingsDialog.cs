using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dedicated window showing all handler associations (extension/protocol → app name).
/// Accessible from the ApplicationsPanel toolbar.
/// </summary>
public partial class HandlerMappingsDialog : Form
{
    private readonly IHandlerMappingService _handlerMappingService;
    private readonly IAppHandlerRegistrationService _handlerRegistrationService;
    private readonly ILoggingService _log;
    private readonly Func<AppDatabase> _getDatabase;
    private readonly Action _saveDatabase;
    private bool _hasChanges;
    private int _ctxRowIndex = -1;
    private readonly GridSortHelper _sortHelper = new();

    private static readonly string[] CommonOptions =
        ["Browser (http, https, .htm, .html)", .. AppHandlerRegistrationService.CommonAssociationSuggestions];

    public HandlerMappingsDialog(
        IHandlerMappingService handlerMappingService,
        IAppHandlerRegistrationService handlerRegistrationService,
        ILoggingService log,
        Func<AppDatabase> getDatabase,
        Action saveDatabase)
    {
        _handlerMappingService = handlerMappingService;
        _handlerRegistrationService = handlerRegistrationService;
        _log = log;
        _getDatabase = getDatabase;
        _saveDatabase = saveDatabase;

        InitializeComponent();

        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _editButton.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxEdit.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);

        Icon = AppIcons.GetAppIcon();
        _sortHelper.EnableThreeStateSorting(_grid, RefreshGrid);

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        var db = _getDatabase();
        var effective = _handlerMappingService.GetEffectiveHandlerMappings(db);

        foreach (var kvp in effective.OrderBy(k => k.Key))
        {
            var app = db.Apps.FirstOrDefault(a => a.Id == kvp.Value);
            var appName = app?.Name ?? $"(unknown: {kvp.Value})";
            var rowIndex = _grid.Rows.Add(kvp.Key, appName);
            _grid.Rows[rowIndex].Tag = kvp;
        }

        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        var hasSelection = _grid.SelectedRows.Count > 0;
        _editButton.Enabled = hasSelection;
        _removeButton.Enabled = hasSelection;
        _ctxEdit.Enabled = hasSelection;
    }

    private void OnGridSelectionChanged(object? sender, EventArgs e) => UpdateButtonState();

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
        {
            _ctxRowIndex = hit.RowIndex;
            _grid.ClearSelection();
            _grid.Rows[hit.RowIndex].Selected = true;
        }
        else
        {
            _ctxRowIndex = -1;
            _grid.ClearSelection();
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _grid.SelectedRows.Count > 0)
            OnRemoveClick(sender, e);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxEdit.Visible = _ctxRowIndex >= 0;
        _ctxRemove.Visible = _ctxRowIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dlg = new Form();
        dlg.Text = "Add Handler Association";
        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
        dlg.MaximizeBox = false;
        dlg.MinimizeBox = false;
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.ClientSize = new Size(350, 140);

        var keyLabel = new Label { Text = "Extension or Protocol:", Location = new Point(15, 12), AutoSize = true };
        var keyCombo = new ComboBox
        {
            Location = new Point(15, 32),
            Size = new Size(320, 23),
            DropDownStyle = ComboBoxStyle.DropDown
        };
        keyCombo.Items.AddRange(CommonOptions.Cast<object>().ToArray());

        var appLabel = new Label { Text = "Application:", Location = new Point(15, 62), AutoSize = true };
        var db = _getDatabase();
        var appCombo = new ComboBox
        {
            Location = new Point(15, 82),
            Size = new Size(320, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var app in db.Apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            appCombo.Items.Add(new AppComboItem(app));
        if (appCombo.Items.Count > 0)
            appCombo.SelectedIndex = 0;

        var okButton = new Button
        {
            Text = "OK", DialogResult = DialogResult.OK,
            Location = new Point(180, 115), Size = new Size(75, 28), FlatStyle = FlatStyle.System
        };
        var cancelButton = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(260, 115), Size = new Size(75, 28), FlatStyle = FlatStyle.System
        };

        dlg.AcceptButton = okButton;
        dlg.CancelButton = cancelButton;
        dlg.Controls.AddRange(keyLabel, keyCombo, appLabel, appCombo, okButton, cancelButton);

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        if (appCombo.SelectedItem is not AppComboItem selectedApp)
            return;

        var rawKey = keyCombo.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(rawKey))
            return;

        // Expand "Browser (...)" convenience option
        string[] keys;
        if (string.Equals(rawKey, CommonOptions[0], StringComparison.OrdinalIgnoreCase))
            keys = ["http", "https", ".htm", ".html"];
        else
            keys = [rawKey];

        foreach (var key in keys)
        {
            if (!AppHandlerRegistrationService.IsValidKey(key))
            {
                MessageBox.Show($"Invalid key '{key}'. Use a file extension (.pdf) or protocol (http).",
                    "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }

            // Auto-enable AllowPassingArguments on the target app
            if (!selectedApp.App.AllowPassingArguments)
                selectedApp.App.AllowPassingArguments = true;

            _handlerMappingService.SetHandlerMapping(key, selectedApp.App.Id, db);
        }

        _hasChanges = true;
        SyncAndRefresh();
    }

    private void OnChangeAppClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is not KeyValuePair<string, string> kvp)
            return;

        using var dlg = new Form();
        dlg.Text = $"Change Application — {kvp.Key}";
        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
        dlg.MaximizeBox = false;
        dlg.MinimizeBox = false;
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.ClientSize = new Size(350, 100);

        var label = new Label { Text = $"Application for \"{kvp.Key}\":", Location = new Point(15, 12), AutoSize = true };
        var db = _getDatabase();
        var appCombo = new ComboBox
        {
            Location = new Point(15, 32),
            Size = new Size(320, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var app in db.Apps.Where(a => a is { IsFolder: false, IsUrlScheme: false }).OrderBy(a => a.Name))
            appCombo.Items.Add(new AppComboItem(app));

        // Pre-select the current app
        for (int i = 0; i < appCombo.Items.Count; i++)
        {
            if (appCombo.Items[i] is AppComboItem item &&
                string.Equals(item.App.Id, kvp.Value, StringComparison.OrdinalIgnoreCase))
            {
                appCombo.SelectedIndex = i;
                break;
            }
        }

        if (appCombo is { SelectedIndex: < 0, Items.Count: > 0 })
            appCombo.SelectedIndex = 0;

        var okButton = new Button
        {
            Text = "OK", DialogResult = DialogResult.OK,
            Location = new Point(175, 62), Size = new Size(75, 28), FlatStyle = FlatStyle.System
        };
        var cancelButton = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(260, 62), Size = new Size(75, 28), FlatStyle = FlatStyle.System
        };

        dlg.AcceptButton = okButton;
        dlg.CancelButton = cancelButton;
        dlg.Controls.AddRange(label, appCombo, okButton, cancelButton);

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        if (appCombo.SelectedItem is not AppComboItem selected)
            return;
        if (string.Equals(selected.App.Id, kvp.Value, StringComparison.OrdinalIgnoreCase))
            return;

        _handlerMappingService.SetHandlerMapping(kvp.Key, selected.App.Id, db);
        if (!selected.App.AllowPassingArguments)
            selected.App.AllowPassingArguments = true;
        _hasChanges = true;
        SyncAndRefresh();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is not KeyValuePair<string, string> kvp)
            return;

        var db = _getDatabase();
        _handlerMappingService.RemoveHandlerMapping(kvp.Key, db);
        _hasChanges = true;
        SyncAndRefresh();
    }

    private void OnOpenDefaultAppsClick(object? sender, EventArgs e)
    {
        try
        {
            ShellHelper.OpenDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to open Default Apps settings: {ex.Message}");
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_hasChanges)
        {
            _saveDatabase();
            if (MessageBox.Show(
                    "Handler registrations have changed. Would you like to open Windows Default Apps settings?",
                    "Handler Associations", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                OnOpenDefaultAppsClick(null, EventArgs.Empty);
            }
        }
    }

    private void SyncAndRefresh()
    {
        var db = _getDatabase();
        var effective = _handlerMappingService.GetEffectiveHandlerMappings(db);
        _handlerRegistrationService.Sync(effective, db.Apps);
        RefreshGrid();
    }

    private class AppComboItem(AppEntry app)
    {
        public AppEntry App { get; } = app;
        public override string ToString() => App.Name;
    }
}
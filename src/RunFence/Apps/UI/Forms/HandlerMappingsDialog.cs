using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dedicated window showing all handler associations (extension/protocol → app or direct handler).
/// Accessible from the ApplicationsPanel toolbar.
/// </summary>
public partial class HandlerMappingsDialog : Form
{
    private readonly HandlerMappingsController _controller;
    private readonly ILoggingService _log;
    private readonly ShellHelper _shellHelper;
    private readonly Func<HandlerMappingAddDialog> _createAddDialog;
    private readonly Func<HandlerMappingEditDirectDialog> _createEditDirectDialog;
    private readonly Func<HandlerMappingEditAppDialog> _createEditAppDialog;
    private Func<AppDatabase> _getDatabase = null!;
    private Action _saveDatabase = null!;
    private int _ctxRowIndex = -1;
    private readonly GridSortHelper _sortHelper = new();

    public HandlerMappingsDialog(
        HandlerMappingsController controller,
        ILoggingService log,
        ShellHelper shellHelper,
        Func<HandlerMappingAddDialog> createAddDialog,
        Func<HandlerMappingEditDirectDialog> createEditDirectDialog,
        Func<HandlerMappingEditAppDialog> createEditAppDialog)
    {
        _controller = controller;
        _log = log;
        _shellHelper = shellHelper;
        _createAddDialog = createAddDialog;
        _createEditDirectDialog = createEditDirectDialog;
        _createEditAppDialog = createEditAppDialog;

        InitializeComponent();

        // Rename Application → Handler without touching Designer.cs
        _colAppName.HeaderText = "Handler";
        _editButton.ToolTipText = "Edit selected association";
        _ctxEdit.Text = "Edit...";

        // Add Import button after a separator
        var sep = new ToolStripSeparator();
        var importButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Import associations from interactive user..."
        };
        importButton.Image = UiIconFactory.CreateToolbarIcon("\u2B07", Color.FromArgb(0x22, 0x6B, 0xBB));
        importButton.Click += OnImportClick;
        var insertIndex = _toolbar.Items.IndexOf(_removeButton) + 1;
        _toolbar.Items.Insert(insertIndex, sep);
        _toolbar.Items.Insert(insertIndex + 1, importButton);

        UpdateWarningLabelHeight();

        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _editButton.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxEdit.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);

        Icon = AppIcons.GetAppIcon();
        _sortHelper.EnableThreeStateSorting(_grid, RefreshGrid);
    }

    /// <summary>
    /// Initializes per-use dialog data. Must be called before <see cref="Form.ShowDialog()"/>.
    /// </summary>
    public void Initialize(Func<AppDatabase> getDatabase, Action saveDatabase, string interactiveUsername)
    {
        _getDatabase = getDatabase;
        _saveDatabase = saveDatabase;
        _openDefaultAppsButton.Text = "Open Default Apps for " + interactiveUsername;
        _controller.Initialize(getDatabase);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var row in _controller.GetGridRows())
        {
            var rowIndex = _grid.Rows.Add(row.Key, row.HandlerDisplay, row.AccountDisplay, row.ArgsTemplate);
            _grid.Rows[rowIndex].Tag = row.Tag;
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

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        var tag = _grid.Rows[e.RowIndex].Tag;
        if (tag is DirectHandlerRowTag)
            OnEditDirectHandlerClick(sender, e);
        else
            OnChangeAppClick(sender, e);
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxEdit.Visible = _ctxRowIndex >= 0;
        _ctxRemove.Visible = _ctxRowIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var db = _getDatabase();
        using var dlg = _createAddDialog();
        dlg.Initialize(db.Apps);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        if (dlg.IsDirectMode)
        {
            var validKeys = ValidateKeys(dlg.ResolvedKeys);
            if (validKeys.Count == 0)
                return;
            _controller.AddDirectHandler(validKeys, dlg.DirectHandlerValue ?? string.Empty);
        }
        else
        {
            if (dlg.SelectedAppId == null)
                return;
            var app = db.Apps.FirstOrDefault(a =>
                string.Equals(a.Id, dlg.SelectedAppId, StringComparison.OrdinalIgnoreCase));
            if (app == null)
                return;
            var validKeys = ValidateKeys(dlg.ResolvedKeys);
            if (validKeys.Count == 0)
                return;
            _controller.AddAppMapping(validKeys, app, dlg.ArgumentsTemplate);
        }

        RefreshGrid();
    }

    private void OnChangeAppClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is DirectHandlerRowTag)
        {
            OnEditDirectHandlerClick(sender, e);
            return;
        }
        if (row.Tag is not AppMappingRowTag appTag)
            return;

        var currentTemplateInRow = row.Cells[3].Value?.ToString();
        var db = _getDatabase();
        var currentApp = db.Apps.FirstOrDefault(a => string.Equals(a.Id, appTag.AppId, StringComparison.OrdinalIgnoreCase));

        using var dlg = _createEditAppDialog();
        dlg.Initialize(appTag.Key, db.Apps, currentApp, currentTemplateInRow);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        if (dlg.SelectedApp is not AppEntry selected)
            return;

        if (_controller.ChangeAppMapping(appTag.Key, appTag.AppId, selected, dlg.NewTemplate, currentTemplateInRow))
            RefreshGrid();
    }

    private void OnEditDirectHandlerClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is not DirectHandlerRowTag tag)
            return;

        var currentEntryNullable = _controller.GetDirectHandlerEntry(tag.Key);
        if (currentEntryNullable == null)
            return;

        var currentEntry = currentEntryNullable.Value;
        var currentValue = currentEntry.ClassName ?? currentEntry.Command ?? string.Empty;

        using var dlg = _createEditDirectDialog();
        dlg.Initialize(tag.Key, currentValue);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var newValue = dlg.NewValue;
        if (newValue == null)
            return;

        _controller.EditDirectHandler(tag.Key, currentEntry, newValue);
        RefreshGrid();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is DirectHandlerRowTag directTag)
        {
            _controller.RemoveDirectHandler(directTag);
            RefreshGrid();
        }
        else if (row.Tag is AppMappingRowTag appTag)
        {
            _controller.RemoveMapping(appTag);
            RefreshGrid();
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        var entries = _controller.GetInteractiveUserAssociations();
        if (entries.Count == 0)
        {
            MessageBox.Show(
                "No user-specific associations found in the interactive user's registry.",
                "Import Associations", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existingKeys = _controller.GetExistingKeys();
        using var importDlg = new ImportAssociationsDialog();
        importDlg.Initialize(entries, existingKeys);

        if (importDlg.ShowDialog(this) != DialogResult.OK)
            return;

        var selected = importDlg.SelectedEntries
            .Where(entry => AppHandlerRegistrationService.IsValidKey(entry.Key))
            .ToList();

        if (selected.Count == 0)
            return;

        _controller.ApplyImportedAssociations(selected);
        RefreshGrid();
    }

    private void OnOpenDefaultAppsClick(object? sender, EventArgs e)
    {
        try
        {
            _shellHelper.OpenDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to open Default Apps settings: {ex.Message}");
        }
    }

    private void OnDialogSizeChanged(object? sender, EventArgs e) => UpdateWarningLabelHeight();

    private void UpdateWarningLabelHeight()
    {
        var textWidth = _warningLabel.Width - _warningLabel.Padding.Horizontal;
        if (textWidth <= 0) return;
        var textSize = TextRenderer.MeasureText(_warningLabel.Text, _warningLabel.Font,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        _warningLabel.Height = textSize.Height + _warningLabel.Padding.Vertical;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_controller.HasChanges)
        {
            // Apply pending AllowPassingArguments changes to live objects before saving
            _controller.ApplyPendingAllowPassingArgs(_getDatabase());
            _saveDatabase();
            if (_controller.HasNewCapability() &&
                MessageBox.Show(
                    "Handler registrations have changed. Would you like to open Windows Default Apps settings?",
                    "Handler Associations", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                OnOpenDefaultAppsClick(null, EventArgs.Empty);
            }
        }
    }

    private void OnReapplyClick(object? sender, EventArgs e)
    {
        _controller.Sync();
        RefreshGrid();
    }

    /// <summary>
    /// Validates keys against <see cref="AppHandlerRegistrationService.IsValidKey"/>.
    /// Shows a warning for each invalid key and returns only the valid subset.
    /// </summary>
    private List<string> ValidateKeys(IReadOnlyList<string> keys)
    {
        var valid = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (AppHandlerRegistrationService.IsValidKey(key))
                valid.Add(key);
            else
                MessageBox.Show($"Invalid key '{key}'. Use a file extension (.pdf) or protocol (http).",
                    "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return valid;
    }
}

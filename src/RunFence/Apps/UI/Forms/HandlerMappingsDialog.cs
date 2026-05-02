using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dedicated window showing all handler associations (extension/protocol → app or direct handler).
/// Accessible from the ApplicationsPanel toolbar.
/// </summary>
public partial class HandlerMappingsDialog : Form
{
    private readonly HandlerMappingMutationHandler _mutationHandler;
    private readonly HandlerMappingSyncService _syncService;
    private readonly IHandlerMappingService _handlerMappingService;
    private readonly IInteractiveUserAssociationReader _interactiveReader;
    private readonly ILoggingService _log;
    private readonly IShellHelper _shellHelper;
    private readonly Func<HandlerMappingAddDialog> _createAddDialog;
    private readonly HandlerMappingGridBuilder _gridBuilder;
    private readonly HandlerMappingDialogHelper _dialogHelper;
    private Func<AppDatabase> _getDatabase = null!;
    private Action _saveDatabase = null!;
    private HashSet<string> _originalRunFenceKeys = null!;
    private bool _hasNewKeys;
    private int _ctxRowIndex = -1;
    private readonly GridSortHelper _sortHelper = new();

    public HandlerMappingsDialog(
        HandlerMappingMutationHandler mutationHandler,
        HandlerMappingSyncService syncService,
        IHandlerMappingService handlerMappingService,
        IInteractiveUserAssociationReader interactiveReader,
        ILoggingService log,
        IShellHelper shellHelper,
        Func<HandlerMappingAddDialog> createAddDialog,
        HandlerMappingGridBuilder gridBuilder,
        HandlerMappingDialogHelper dialogHelper)
    {
        _mutationHandler = mutationHandler;
        _syncService = syncService;
        _handlerMappingService = handlerMappingService;
        _interactiveReader = interactiveReader;
        _log = log;
        _shellHelper = shellHelper;
        _createAddDialog = createAddDialog;
        _gridBuilder = gridBuilder;
        _dialogHelper = dialogHelper;

        InitializeComponent();

        // Rename Application → Handler without touching Designer.cs
        colAppName.HeaderText = "Handler";
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
        _originalRunFenceKeys = new HashSet<string>(
            _handlerMappingService.GetAllHandlerMappings(getDatabase()).Keys,
            StringComparer.OrdinalIgnoreCase);
        _mutationHandler.Changed += OnMutationHandlerChanged;
        _syncService.Initialize(_mutationHandler);
        RefreshGrid();
    }

    private void OnMutationHandlerChanged()
    {
        _saveDatabase();
        if (_dialogHelper.HasNewCapability(_getDatabase, _originalRunFenceKeys))
            _hasNewKeys = true;
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var row in _gridBuilder.GetGridRows(_getDatabase()))
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
        if (_grid.Rows[e.RowIndex].Tag is DirectHandlerRowTag)
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
            var (validKeys, invalidKeys) = _dialogHelper.ValidateKeys(dlg.ResolvedKeys);
            if (invalidKeys.Count > 0)
                MessageBox.Show($"Invalid keys: {string.Join(", ", invalidKeys)}. Use file extensions (.pdf) or protocols (http).",
                    "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (validKeys.Count == 0)
                return;
            var handlerValue = dlg.DirectHandlerValue ?? string.Empty;
            var resolvedEntries = validKeys
                .Select(key => _dialogHelper.ResolveDirectHandlerEntry(key, handlerValue))
                .ToList();
            _mutationHandler.AddDirectHandler(validKeys, resolvedEntries);
        }
        else
        {
            if (dlg.SelectedApp is not {} app)
                return;
            var (validKeys, invalidKeys) = _dialogHelper.ValidateKeys(dlg.ResolvedKeys);
            if (invalidKeys.Count > 0)
                MessageBox.Show($"Invalid keys: {string.Join(", ", invalidKeys)}. Use file extensions (.pdf) or protocols (http).",
                    "Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (validKeys.Count == 0)
                return;
            _mutationHandler.UpdateAppPrefixes(app.Id, dlg.AppPrefixes);
            _mutationHandler.AddAppMapping(validKeys, app, dlg.ArgumentsTemplate, dlg.PathPrefixes, dlg.ReplacePrefixes);
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

        using var dlg = _createAddDialog();
        dlg.InitializeForEditApp(appTag.Key, db.Apps, currentApp, currentTemplateInRow,
            currentApp?.PathPrefixes?.AsReadOnly(), appTag.PathPrefixes, appTag.ReplacePrefixes);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        if (dlg.SelectedApp is not {} selected)
            return;

        _mutationHandler.UpdateAppPrefixes(selected.Id, dlg.AppPrefixes);
        if (_mutationHandler.ChangeAppMapping(appTag.Key, appTag.AppId, selected, dlg.ArgumentsTemplate,
                currentTemplateInRow, appTag.PathPrefixes, dlg.PathPrefixes,
                appTag.ReplacePrefixes, dlg.ReplacePrefixes))
            RefreshGrid();
    }

    private void OnEditDirectHandlerClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is not DirectHandlerRowTag tag)
            return;

        var currentEntryOrNull = _dialogHelper.GetCurrentDirectHandler(tag.Key, _getDatabase);
        if (currentEntryOrNull == null)
            return;

        var currentEntry = currentEntryOrNull.Value;
        var currentValue = currentEntry.ClassName ?? currentEntry.Command ?? string.Empty;

        using var dlg = _createAddDialog();
        dlg.InitializeForEditDirect(tag.Key, currentValue);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var newValue = dlg.DirectHandlerValue;
        if (newValue == null)
            return;

        var newEntry = _dialogHelper.ResolveDirectHandlerEntry(tag.Key, newValue);
        _mutationHandler.EditDirectHandler(tag.Key, currentEntry, newEntry);
        RefreshGrid();
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        switch (row.Tag)
        {
            case DirectHandlerRowTag directTag:
                _mutationHandler.RemoveDirectHandler(directTag);
                RefreshGrid();
                break;
            case AppMappingRowTag appTag:
                _mutationHandler.RemoveMapping(appTag);
                RefreshGrid();
                break;
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        var entries = _interactiveReader.GetInteractiveUserAssociations();
        if (entries.Count == 0)
        {
            MessageBox.Show(
                "No user-specific associations found in the interactive user's registry.",
                "Import Associations", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var db = _getDatabase();
        var existingKeys = _gridBuilder.GetExistingKeys(db);
        using var importDlg = new ImportAssociationsDialog();
        importDlg.Initialize(entries, existingKeys);

        if (importDlg.ShowDialog(this) != DialogResult.OK)
            return;

        var selected = importDlg.SelectedEntries
            .Where(entry => AppHandlerRegistrationService.IsValidKey(entry.Key))
            .ToList();

        if (selected.Count == 0)
            return;

        _mutationHandler.ApplyImportedAssociations(selected);
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
        _mutationHandler.Changed -= OnMutationHandlerChanged;
        _syncService.Detach();

        // All mutations are saved immediately via the Changed subscription.
        // Only prompt for Default Apps when new keys were added during this session.
        if (_hasNewKeys &&
            MessageBox.Show(
                "Handler registrations have changed. Would you like to open Windows Default Apps settings?",
                "Handler Associations", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            OnOpenDefaultAppsClick(null, EventArgs.Empty);
        }
    }

    private void OnReapplyClick(object? sender, EventArgs e)
    {
        _syncService.Sync();
        RefreshGrid();
    }
}

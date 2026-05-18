using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dedicated window showing all handler associations (extension/protocol â†’ app or direct handler).
/// Accessible from the ApplicationsPanel toolbar.
/// </summary>
public partial class HandlerMappingsDialog : RunFence.UI.Forms.ContextHelpForm
{
    private readonly HandlerMappingMutationHandler _mutationHandler;
    private readonly HandlerMappingSyncService _syncService;
    private readonly IHandlerMappingService _handlerMappingService;
    private readonly IInteractiveUserAssociationReader _interactiveReader;
    private readonly ILoggingService _log;
    private readonly IMessageBoxService _messageBoxService;
    private readonly IShellHelper _shellHelper;
    private readonly HandlerMappingGridBuilder _gridBuilder;
    private readonly HandlerMappingsChildDialogCoordinator _childDialogCoordinator;
    private IHandlerMappingDialogPersistence _persistence = null!;
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
        IMessageBoxService messageBoxService,
        IShellHelper shellHelper,
        HandlerMappingGridBuilder gridBuilder,
        HandlerMappingsChildDialogCoordinator childDialogCoordinator)
    {
        _mutationHandler = mutationHandler;
        _syncService = syncService;
        _handlerMappingService = handlerMappingService;
        _interactiveReader = interactiveReader;
        _log = log;
        _messageBoxService = messageBoxService;
        _shellHelper = shellHelper;
        _gridBuilder = gridBuilder;
        _childDialogCoordinator = childDialogCoordinator;

        InitializeComponent();

        colAppName.HeaderText = "Handler";
        _editButton.ToolTipText = "Edit selected association";
        _ctxEdit.Text = "Edit...";

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
        SetContextHelp(_contextHelpButton, ContextHelpTextResolver.InstructionText);
        SetContextHelp(_grid, ContextHelpTextCatalog.App_HandlerMappings);
    }

    public void Initialize(IHandlerMappingDialogPersistence persistence, string interactiveUsername)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _openDefaultAppsButton.Text = "Open Default Apps for " + interactiveUsername;
        _originalRunFenceKeys = new HashSet<string>(
            _handlerMappingService.GetAllHandlerMappings(_persistence.GetDatabase()).Keys,
            StringComparer.OrdinalIgnoreCase);
        RefreshGrid();
    }

    private void SyncAndPersistRemovedAppMapping(RemovedAppMappingState removed)
    {
        var keysToRestore = removed.RequiresRestore ? new[] { removed.Key } : Array.Empty<string>();
        if (!ShowSyncWarningIfNeeded(
                keysToRestore,
                "RunFence could not remove the handler association because registry synchronization failed. The change was reverted:"))
        {
            _mutationHandler.RestoreRemovedAppMapping(_persistence.GetDatabase(), removed);
            RefreshGrid();
            return;
        }

        try
        {
            _persistence.SaveDatabase();
        }
        catch (Exception ex)
        {
            ShowOperationError(this, "save handler association removals", ex.Message);
        }

        RefreshGrid();
    }

    private void SyncAndPersistRemovedDirectHandler(RemovedDirectHandlerState removed)
    {
        var keysToRestore = removed.RequiresRestore ? new[] { removed.Key } : Array.Empty<string>();
        if (!ShowSyncWarningIfNeeded(
                keysToRestore,
                "RunFence could not remove the handler association because registry synchronization failed. The change was reverted:"))
        {
            _mutationHandler.RestoreRemovedDirectHandler(_persistence.GetDatabase(), removed);
            RefreshGrid();
            return;
        }

        try
        {
            _persistence.SaveDatabase();
        }
        catch (Exception ex)
        {
            ShowOperationError(this, "save handler association removals", ex.Message);
        }

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var row in _gridBuilder.GetGridRows(_persistence.GetDatabase()))
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
            if (hit.ColumnIndex >= 0)
                _grid.CurrentCell = _grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
        }
        else
        {
            _ctxRowIndex = -1;
            _grid.ClearSelection();
            _grid.CurrentCell = null;
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
        if (_ctxRowIndex < 0)
        {
            _grid.ClearSelection();
            _grid.CurrentCell = null;
        }

        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxEdit.Visible = _ctxRowIndex >= 0;
        _ctxRemove.Visible = _ctxRowIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dialog = _childDialogCoordinator.CreateAddDialog(_persistence);
        var dialogResult = dialog.ShowDialog(this);
        HandleChildDialogClosed(dialogResult, dialog.HasUnresolvedSubmitFailure);
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

        using var dialog = _childDialogCoordinator.CreateEditAppDialog(
            appTag,
            row.Cells[3].Value?.ToString(),
            _persistence);
        var dialogResult = dialog.ShowDialog(this);
        HandleChildDialogClosed(dialogResult, dialog.HasUnresolvedSubmitFailure);
    }

    private void OnEditDirectHandlerClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;

        var row = _grid.SelectedRows[0];
        if (row.Tag is not DirectHandlerRowTag tag)
            return;

        using var dialog = _childDialogCoordinator.CreateEditDirectDialog(tag, _persistence);
        if (dialog == null)
            return;

        var dialogResult = dialog.ShowDialog(this);
        HandleChildDialogClosed(dialogResult, dialog.HasUnresolvedSubmitFailure);
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;

        var row = _grid.SelectedRows[0];
        switch (row.Tag)
        {
            case DirectHandlerRowTag directTag:
                var removedDirect = _mutationHandler.RemoveDirectHandler(_persistence.GetDatabase(), directTag);
                if (removedDirect != null)
                    SyncAndPersistRemovedDirectHandler(removedDirect);
                break;
            case AppMappingRowTag appTag:
                var removedApp = _mutationHandler.RemoveMapping(_persistence.GetDatabase(), appTag);
                if (removedApp != null)
                    SyncAndPersistRemovedAppMapping(removedApp);
                break;
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        var entries = _interactiveReader.GetInteractiveUserAssociations();
        if (entries.Count == 0)
        {
            _messageBoxService.Show(
                this,
                "No user-specific associations found in the interactive user's registry.",
                "Import Associations",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = _childDialogCoordinator.CreateImportDialog(entries, _persistence);
        var dialogResult = dialog.ShowDialog(this);
        HandleChildDialogClosed(dialogResult, dialog.HasUnresolvedSubmitFailure);
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
        if (textWidth <= 0)
            return;

        var textSize = TextRenderer.MeasureText(
            _warningLabel.Text,
            _warningLabel.Font,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        _warningLabel.Height = textSize.Height + _warningLabel.Padding.Vertical;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_hasNewKeys &&
            _messageBoxService.Show(
                this,
                "Handler registrations have changed. Would you like to open Windows Default Apps settings?",
                "Handler Associations",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            OnOpenDefaultAppsClick(null, EventArgs.Empty);
        }
    }

    private void OnReapplyClick(object? sender, EventArgs e)
    {
        ShowSyncWarningIfNeeded(null, "RunFence could not reapply handler registrations:");
        RefreshGrid();
    }

    private bool ShowSyncWarningIfNeeded(IReadOnlyList<string>? keysToRestore, string warningPrefix)
    {
        var syncResult = _syncService.Sync(keysToRestore);
        if (syncResult.Succeeded)
            return true;

        _log.Warn($"Handler association registry sync failed: {syncResult.WarningMessage}");
        _messageBoxService.Show(
            this,
            $"{warningPrefix}\n\n{syncResult.WarningMessage}",
            "Handler Associations",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private void HandleChildDialogClosed(DialogResult dialogResult, bool hasUnresolvedSubmitFailure)
    {
        var closeResult = _childDialogCoordinator.HandleChildDialogClosed(
            dialogResult,
            hasUnresolvedSubmitFailure,
            _persistence,
            _originalRunFenceKeys);
        if (closeResult.HasNewCapability)
            _hasNewKeys = true;
        if (closeResult.ShouldRefresh)
            RefreshGrid();
    }

    private void ShowOperationError(IWin32Window owner, string action, string? message)
    {
        _messageBoxService.Show(
            owner,
            $"RunFence could not {action}:\n\n{message}",
            "Save Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

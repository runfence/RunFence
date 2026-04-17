using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles selection, tab switching, context menus, keyboard/mouse navigation, and path
/// helpers for <see cref="AclManagerDialog"/>. The form-closing guard checks apply and
/// pending state; scan cancellation is delegated to the caller.
/// </summary>
public class AclManagerSelectionHandler(
    AclManagerGrantsHelper grantsHelper,
    AclManagerTraverseHelper traverseHelper,
    AclManagerActionOrchestrator actionHandler,
    AclManagerApplyOrchestrator applyHandler,
    ShellHelper shellHelper)
{
    private readonly AclManagerGrantsHelper _grantsHelper = grantsHelper;
    private readonly AclManagerTraverseHelper _traverseHelper = traverseHelper;
    private readonly AclManagerActionOrchestrator _actionHandler = actionHandler;
    private readonly AclManagerApplyOrchestrator _applyHandler = applyHandler;
    private readonly ShellHelper _shellHelper = shellHelper;
    private IWin32Window _owner = null!;
    private bool _isContainer;
    private AclManagerPendingChanges _pending = null!;
    private AclManagerDialogControls _controls = null!;
    private Action _refreshTraverseGrid = null!;

    /// <summary>Raised when the user presses Delete with the Remove button enabled and no cell in edit mode.</summary>
    public event EventHandler? RemoveKeyPressed;

    public void Initialize(
        IWin32Window owner,
        bool isContainer,
        AclManagerPendingChanges pending,
        AclManagerDialogControls controls,
        Action refreshTraverseGrid)
    {
        _owner = owner;
        _isContainer = isContainer;
        _pending = pending;
        _controls = controls;
        _refreshTraverseGrid = refreshTraverseGrid;
    }

    // --- Tab / Selection ---

    public void HandleTabChanged()
    {
        bool isTraverseTab = _controls.TabControl.SelectedTab == _controls.TraverseTab;
        _controls.AddFileButton.ToolTipText = isTraverseTab ? "Add File (Traverse)" : "Add File";
        _controls.AddFolderButton.ToolTipText = isTraverseTab ? "Add Folder (Traverse)" : "Add Folder";
        UpdateActionButtons();
    }

    public void HandleSelectionChanged() => UpdateActionButtons();

    public void UpdateActionButtons()
    {
        bool isTraverseTab = _controls.TabControl.SelectedTab == _controls.TraverseTab;

        if (isTraverseTab)
        {
            var expanded = AclManagerSectionHeader.ExpandSectionSelection(_controls.TraverseGrid);
            _controls.RemoveButton.Enabled = expanded.Any(r => r.Tag is GrantedPathEntry);
            _controls.FixAclsButton.Enabled = expanded.Any(r => r.Tag is GrantedPathEntry entry &&
                                                                _traverseHelper.FixableEntries.Contains(entry));
        }
        else
        {
            var expanded = AclManagerSectionHeader.ExpandSectionSelection(_controls.GrantsGrid);
            _controls.RemoveButton.Enabled = expanded.Count > 0;
            _controls.FixAclsButton.Enabled = expanded.Any(r =>
                r.Tag is GrantedPathEntry e && _grantsHelper.FixableEntries.Contains(e));
        }

        _controls.ApplyButton.Enabled = _pending.HasPendingChanges;
    }

    // --- Cell value changes ---

    public void HandleGrantsCellValueChanged(DataGridViewCellEventArgs e)
    {
        if (_grantsHelper.IsSuppressed || e.RowIndex < 0)
            return;
        var row = _controls.GrantsGrid.Rows[e.RowIndex];
        if (row.Tag is not GrantedPathEntry entry)
            return;

        var colName = _controls.GrantsGrid.Columns[e.ColumnIndex].Name;

        if (colName == AclManagerGrantsHelper.ColOwner && !_isContainer)
        {
            _actionHandler.HandleOwnChange(row, entry, UpdateActionButtons);
            return;
        }

        bool traverseChanged = _grantsHelper.HandleCellValueChanged(row, colName, entry,
            msg => MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
        if (traverseChanged)
            _refreshTraverseGrid();
        UpdateActionButtons();
    }

    public void HandleGrantsDirtyStateChanged()
    {
        if (_grantsHelper.IsSuppressed)
            return;
        if (!_controls.GrantsGrid.IsCurrentCellDirty)
            return;
        _controls.GrantsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    // --- Form closing ---

    /// <summary>
    /// Returns true if closing should be cancelled (apply in progress or user declined).
    /// The caller is responsible for cancelling any in-flight scan operation.
    /// </summary>
    public bool HandleFormClosing()
    {
        if (_applyHandler.IsApplyInProgress)
        {
            MessageBox.Show("Apply is in progress, please wait.", "ACL Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true; // cancel
        }

        if (_pending.HasPendingChanges)
        {
            var result = MessageBox.Show(
                "You have unapplied changes. Discard and close?",
                "ACL Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return true; // cancel
        }

        return false; // don't cancel
    }

    // --- Context menus ---

    public void HandleGrantsContextMenuOpening()
    {
        var selectedEntryRows = AclManagerSectionHeader.ExpandSectionSelection(_controls.GrantsGrid);
        bool hasItem = selectedEntryRows.Count > 0;
        bool singleItem = _controls.GrantsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Count(r => r.Tag is GrantedPathEntry) == 1;
        bool hasFixable = selectedEntryRows.Any(r =>
            r.Tag is GrantedPathEntry ge && _grantsHelper.FixableEntries.Contains(ge));
        bool hasDbItem = _controls.GrantsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Any(r => r.Tag is GrantedPathEntry ge && !_pending.IsPendingAdd(ge.Path, ge.IsDeny));

        _controls.CtxAddFile.Visible = !hasItem;
        _controls.CtxAddFolder.Visible = !hasItem;
        _controls.CtxGrantsSep.Visible = !hasItem;
        _controls.CtxRemove.Visible = hasItem;
        _controls.CtxUntrack.Visible = hasDbItem;
        _controls.CtxFixAcls.Visible = hasFixable;
        _controls.CtxGrantsOpenFolderSep.Visible = singleItem;
        _controls.CtxOpenFolderGrants.Visible = singleItem;
        _controls.CtxCopyPathGrants.Visible = singleItem;
        _controls.CtxGrantsPropertiesSep.Visible = singleItem;
        _controls.CtxPropertiesGrants.Visible = singleItem;
    }

    public void HandleTraverseContextMenuOpening()
    {
        var expanded = AclManagerSectionHeader.ExpandSectionSelection(_controls.TraverseGrid);
        bool hasItem = expanded.Any(r => r.Tag is GrantedPathEntry);
        bool hasFixable = hasItem && expanded.Any(r => r.Tag is GrantedPathEntry te &&
                                                       _traverseHelper.FixableEntries.Contains(te));
        bool singleItem = _controls.TraverseGrid.SelectedRows.Cast<DataGridViewRow>()
            .Count(r => r.Tag is GrantedPathEntry) == 1;
        bool hasDbItem = _controls.TraverseGrid.SelectedRows.Cast<DataGridViewRow>()
            .Any(r => r.Tag is GrantedPathEntry te && !_pending.IsPendingTraverseAdd(te.Path));

        _controls.CtxTraverseAddFile.Visible = !hasItem;
        _controls.CtxTraverseAddFolder.Visible = !hasItem;
        _controls.CtxTraverseSep.Visible = !hasItem;
        _controls.CtxTraverseRemove.Visible = hasItem;
        _controls.CtxTraverseUntrack.Visible = hasDbItem;
        _controls.CtxTraverseFixAcls.Visible = hasFixable;
        _controls.CtxTraverseOpenFolderSep.Visible = singleItem;
        _controls.CtxTraverseOpenFolder.Visible = singleItem;
        _controls.CtxTraverseCopyPath.Visible = singleItem;
        _controls.CtxTraversePropertiesSep.Visible = singleItem;
        _controls.CtxTraverseProperties.Visible = singleItem;
    }

    // --- Grid key / mouse ---

    public void HandleGridKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || !_controls.RemoveButton.Enabled)
            return;
        if (_controls.GrantsGrid.IsCurrentCellInEditMode || _controls.TraverseGrid.IsCurrentCellInEditMode)
            return;
        RemoveKeyPressed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    public void HandleGrantsRightClickDown(MouseEventArgs e)
        => HandleRightClickDown(e, _controls.GrantsGrid);

    public void HandleTraverseRightClickDown(MouseEventArgs e)
        => HandleRightClickDown(e, _controls.TraverseGrid);

    private static void HandleRightClickDown(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = grid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
        {
            var row = grid.Rows[hit.RowIndex];
            if (!row.Selected)
            {
                grid.ClearSelection();
                row.Selected = true;
            }
        }
        else
        {
            grid.ClearSelection();
        }
    }

    public void HandleGrantsMouseClick(MouseEventArgs e)
        => OnGridMouseClick(e, _controls.GrantsGrid);

    public void HandleTraverseMouseClick(MouseEventArgs e)
        => OnGridMouseClick(e, _controls.TraverseGrid);

    private static void OnGridMouseClick(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button == MouseButtons.Left && grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None)
            grid.ClearSelection();
    }

    // --- Path helpers for context menu ---

    private string? GetSelectedGrantPath()
    {
        var selected = _controls.GrantsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is GrantedPathEntry).ToList();
        return selected.Count == 1 ? ((GrantedPathEntry)selected[0].Tag!).Path : null;
    }

    private string? GetSelectedTraversePath()
    {
        var selected = _controls.TraverseGrid.SelectedRows.Cast<DataGridViewRow>().ToList();
        return selected.Count == 1 && selected[0].Tag is GrantedPathEntry e ? e.Path : null;
    }

    public void OpenFolderGrants()
        => _actionHandler.OpenInExplorer(GetSelectedGrantPath()!);

    public void CopyPathGrants()
        => Clipboard.SetText(GetSelectedGrantPath()!);

    public void ShowPropertiesGrants()
        => _shellHelper.ShowProperties(GetSelectedGrantPath()!, _owner);

    public void OpenFolderTraverse()
        => _actionHandler.OpenInExplorer(GetSelectedTraversePath()!);

    public void CopyPathTraverse()
        => Clipboard.SetText(GetSelectedTraversePath()!);

    public void ShowPropertiesTraverse()
        => _shellHelper.ShowProperties(GetSelectedTraversePath()!, _owner);
}
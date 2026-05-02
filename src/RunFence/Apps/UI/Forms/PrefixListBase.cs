using System.ComponentModel;
using RunFence.UI;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Base UserControl providing a shared ToolStrip (add-folder, add-manual, remove) and a single-column
/// editable <see cref="StyledDataGridView"/> for prefix list editors.
/// Derived classes are responsible for placing <see cref="_toolStrip"/> and <see cref="_dataGrid"/>
/// inside their own container (GroupBox etc.) and overriding the virtual members to adapt behavior.
/// </summary>
public partial class PrefixListBase : UserControl
{
    private bool _runtimeInitialized;

    /// <summary>
    /// Assigns toolbar and context-menu icons and applies any other runtime-only UI state.
    /// Idempotent: safe to call multiple times; skips initialization inside the designer.
    /// Derived classes must call this before any public method or override that reads toolbar or grid state.
    /// After setting up base UI state, calls <see cref="OnRuntimeInitialize"/> for derived-class setup.
    /// </summary>
    protected void EnsurePrefixListRuntimeInitialized()
    {
        if (_runtimeInitialized || DesignMode) return;
        _runtimeInitialized = true;
        _addButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xFF, 0xC2, 0x00));
        _addManualButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x33, 0x66, 0x99));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxAdd.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xFF, 0xC2, 0x00), 16);
        _ctxAddManual.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        OnRuntimeInitialize();
    }

    /// <summary>
    /// Called once from <see cref="EnsurePrefixListRuntimeInitialized"/> after base icon setup completes.
    /// Override in derived classes to perform additional runtime-only initialization.
    /// </summary>
    protected virtual void OnRuntimeInitialize() { }

    /// <summary>Called when the user clicks the folder-browse add button or the corresponding context menu item.</summary>
    protected virtual void PerformAdd(string path) { }

    /// <summary>Called when the user clicks the manual-entry add button or the corresponding context menu item.</summary>
    protected virtual void PerformAddManual() { }

    /// <summary>Returns true when the current row may be removed. Used to enable/disable the remove button.</summary>
    protected virtual bool CanRemoveCurrentRow() => _dataGrid.CurrentRow != null;

    /// <summary>Removes the currently selected row. Override for custom section-aware behavior.</summary>
    protected virtual void PerformRemove()
    {
        if (_dataGrid.CurrentRow != null)
            _dataGrid.Rows.Remove(_dataGrid.CurrentRow);
    }

    /// <summary>Updates the remove button enabled state. Called on selection change.</summary>
    protected virtual void UpdateRemoveButton()
    {
        _removeButton.Enabled = CanRemoveCurrentRow();
    }

    /// <summary>Configures context menu item visibility before the menu opens.</summary>
    protected virtual void SetupContextMenu()
    {
        _ctxAdd.Visible = true;
        _ctxAddManual.Visible = true;
        _ctxRemove.Visible = CanRemoveCurrentRow();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { ShowNewFolderButton = false };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var path = dlg.SelectedPath;
        if (!path.EndsWith('\\')) path += "\\";
        PerformAdd(path);
    }

    private void OnAddManualClick(object? sender, EventArgs e) => PerformAddManual();

    private void OnRemoveClick(object? sender, EventArgs e) => PerformRemove();

    private void OnSelectionChanged(object? sender, EventArgs e) => UpdateRemoveButton();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && !_dataGrid.IsCurrentCellInEditMode)
            PerformRemove();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _dataGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
            _dataGrid.CurrentCell = _dataGrid.Rows[hit.RowIndex].Cells[0];
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e) => SetupContextMenu();
}

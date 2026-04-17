using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles drag-and-drop reordering of grant entries between config sections in the
/// grants and traverse grids of <see cref="AclManagerDialog"/>. Uses pure mouse-event
/// tracking (no OLE DoDragDrop) so the grids can have AllowDrop=false and receive
/// cross-IL shell file drops via WM_DROPFILES.
/// </summary>
public class AclManagerDragDropHandler(IGrantConfigTracker grantConfigTracker)
{
    private string _sid = null!;
    private AclManagerPendingChanges _pending = null!;

    private Point _dragStartPoint;
    private GrantedPathEntry? _draggingEntry;
    private DataGridViewRow? _dropTargetRow;

    public void Initialize(
        string sid,
        AclManagerPendingChanges pending)
    {
        _sid = sid;
        _pending = pending;
    }

    public void HandleMouseDown(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragStartPoint = e.Location;
        _draggingEntry = null;
    }

    public void HandleMouseMove(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_draggingEntry == null)
        {
            if (Math.Abs(e.X - _dragStartPoint.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStartPoint.Y) < SystemInformation.DragSize.Height)
                return;

            var hitTest = grid.HitTest(_dragStartPoint.X, _dragStartPoint.Y);
            if (hitTest.RowIndex < 0)
                return;
            if (grid.Rows[hitTest.RowIndex].Tag is not GrantedPathEntry entry)
                return;

            _draggingEntry = entry;
            grid.Cursor = Cursors.SizeAll;
        }

        UpdateDropHighlight(e.X, e.Y, grid);
    }

    /// <summary>
    /// Completes the drag. Returns true if an entry was moved (caller should refresh grids
    /// and update the Apply button state).
    /// </summary>
    public bool HandleMouseUp(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button != MouseButtons.Left)
            return false;
        grid.Cursor = Cursors.Default;

        var draggedEntry = _draggingEntry;
        _draggingEntry = null;
        ClearDropHighlight();

        if (draggedEntry == null)
            return false;

        var hitTest = grid.HitTest(e.X, e.Y);
        if (hitTest.RowIndex < 0)
            return false;

        var (targetConfigPath, _) = GetSectionForGrid(grid, hitTest.RowIndex);
        if (string.Equals(targetConfigPath, GetEffectiveConfigPath(draggedEntry), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!draggedEntry.IsTraverseOnly)
        {
            var normalizedPath = Path.GetFullPath(draggedEntry.Path);
            var configMoveKey = (normalizedPath, _pending.GetEffectiveIsDeny(draggedEntry));
            var removeKey = (normalizedPath, draggedEntry.IsDeny);
            _pending.PendingConfigMoves[configMoveKey] = targetConfigPath;
            _pending.PendingRemoves.Remove(removeKey);
        }
        else
        {
            var path = Path.GetFullPath(draggedEntry.Path);
            _pending.PendingTraverseConfigMoves[path] = targetConfigPath;
            _pending.PendingTraverseRemoves.Remove(path);
        }

        return true;
    }

    private void UpdateDropHighlight(int x, int y, DataGridView grid)
    {
        var hitTest = grid.HitTest(x, y);
        if (hitTest.RowIndex < 0)
        {
            ClearDropHighlight();
            return;
        }

        var (targetConfigPath, headerRow) = GetSectionForGrid(grid, hitTest.RowIndex);
        if (headerRow == null || string.Equals(targetConfigPath, GetEffectiveConfigPath(_draggingEntry!), StringComparison.OrdinalIgnoreCase))
        {
            ClearDropHighlight();
            return;
        }

        SetDropHighlight(headerRow);
    }

    private void ClearDropHighlight()
    {
        if (_dropTargetRow == null)
            return;
        _dropTargetRow.DefaultCellStyle.BackColor = AclManagerSectionHeader.SectionHeaderBackColor;
        _dropTargetRow.DefaultCellStyle.SelectionBackColor = AclManagerSectionHeader.SectionHeaderBackColor;
        _dropTargetRow = null;
    }

    private void SetDropHighlight(DataGridViewRow headerRow)
    {
        if (_dropTargetRow == headerRow)
            return;
        ClearDropHighlight();
        _dropTargetRow = headerRow;
        headerRow.DefaultCellStyle.BackColor = Color.FromArgb(0xB8, 0xC8, 0xF0);
        headerRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xA0, 0xB5, 0xE5);
    }

    private string? GetEffectiveConfigPath(GrantedPathEntry entry) =>
        _pending.GetEffectiveConfigPath(entry, grantConfigTracker, _sid);

    private static (string? ConfigPath, DataGridViewRow? HeaderRow) GetSectionForGrid(DataGridView grid, int rowIndex)
    {
        for (int i = rowIndex; i >= 0; i--)
        {
            if (grid.Rows[i].Tag is ConfigSectionHeader header)
                return (header.ConfigPath, grid.Rows[i]);
        }

        return (null, null);
    }
}
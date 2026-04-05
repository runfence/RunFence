using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles drag-and-drop reordering of app entries between config sections in the
/// ApplicationsPanel grid. Uses pure mouse-event tracking (no OLE DoDragDrop) so the
/// grid can have AllowDrop=false and receive cross-IL shell file drops via WM_DROPFILES.
/// </summary>
public class AppGridDragDropHandler
{
    private readonly IAppConfigService _appConfigService;
    private DataGridView _grid = null!;
    private IApplicationsPanelState _state = null!;
    private Action<string> _assignAndSave = null!;

    private DataGridViewRow? _dropTargetHeaderRow;
    private Point _dragStartPoint;
    private AppEntry? _draggingApp;

    public AppGridDragDropHandler(IAppConfigService appConfigService)
    {
        _appConfigService = appConfigService;
    }

    public void Initialize(DataGridView grid, IApplicationsPanelState state, Action<string> assignAndSave)
    {
        _grid = grid;
        _state = state;
        _assignAndSave = assignAndSave;
    }

    public void HandleMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragStartPoint = e.Location;
        _draggingApp = null;
    }

    public void HandleMouseMove(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        if (!_appConfigService.HasLoadedConfigs || _state.IsSortActive)
            return;

        if (_draggingApp == null)
        {
            var dragSize = SystemInformation.DragSize;
            var dragRect = new Rectangle(
                _dragStartPoint.X - dragSize.Width / 2,
                _dragStartPoint.Y - dragSize.Height / 2,
                dragSize.Width, dragSize.Height);
            if (dragRect.Contains(e.Location))
                return;

            var hitTest = _grid.HitTest(_dragStartPoint.X, _dragStartPoint.Y);
            if (hitTest.RowIndex < 0)
                return;
            if (_grid.Rows[hitTest.RowIndex].Tag is not AppEntry app)
                return;

            _draggingApp = app;
            _grid.Cursor = Cursors.SizeAll;
        }

        UpdateDropHighlight(e.X, e.Y);
    }

    public void HandleMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _grid.Cursor = Cursors.Default;

        var draggedApp = _draggingApp;
        _draggingApp = null;
        ClearDropHighlight();

        if (draggedApp == null)
            return;

        var hitTest = _grid.HitTest(e.X, e.Y);
        if (hitTest.RowIndex < 0)
            return;

        var (targetConfigPath, _) = GetSectionForRow(hitTest.RowIndex);
        var currentConfigPath = _appConfigService.GetConfigPath(draggedApp.Id);
        if (string.Equals(targetConfigPath, currentConfigPath, StringComparison.OrdinalIgnoreCase))
            return;

        _appConfigService.AssignApp(draggedApp.Id, targetConfigPath);
        _assignAndSave(draggedApp.Id);
    }

    /// <summary>Called before grid rows are cleared to avoid stale row references.</summary>
    public void ClearDropTargetOnRowsClear() => _dropTargetHeaderRow = null;

    private void UpdateDropHighlight(int x, int y)
    {
        var hitTest = _grid.HitTest(x, y);
        if (hitTest.RowIndex < 0)
        {
            ClearDropHighlight();
            return;
        }

        var (targetConfigPath, headerRow) = GetSectionForRow(hitTest.RowIndex);
        if (headerRow == null)
        {
            ClearDropHighlight();
            return;
        }

        var currentConfigPath = _appConfigService.GetConfigPath(_draggingApp!.Id);
        if (string.Equals(targetConfigPath, currentConfigPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearDropHighlight();
            return;
        }

        SetDropHighlight(headerRow);
    }

    private (string? ConfigPath, DataGridViewRow? HeaderRow) GetSectionForRow(int rowIndex)
    {
        for (int i = rowIndex; i >= 0; i--)
        {
            if (_grid.Rows[i].Tag is ApplicationsPanel.ConfigGroupHeaderTag header)
                return (header.ConfigPath, _grid.Rows[i]);
        }

        return (null, null);
    }

    private void SetDropHighlight(DataGridViewRow headerRow)
    {
        if (_dropTargetHeaderRow == headerRow)
            return;
        ClearDropHighlight();
        _dropTargetHeaderRow = headerRow;
        headerRow.DefaultCellStyle.BackColor = Color.FromArgb(0xB8, 0xC8, 0xF0);
        headerRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xA0, 0xB5, 0xE5);
    }

    private void ClearDropHighlight()
    {
        if (_dropTargetHeaderRow == null)
            return;
        _dropTargetHeaderRow.DefaultCellStyle.BackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        _dropTargetHeaderRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xD0, 0xD8, 0xEC);
        _dropTargetHeaderRow = null;
    }
}
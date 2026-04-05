using RunFence.Apps.Shortcuts;

namespace RunFence.Account.UI;

/// <summary>
/// Handles all cell-level and row-level custom painting for process sub-rows
/// in the accounts grid, including the toggle glyph, tree lines, cell suppression,
/// and per-process icon + text rendering.
/// </summary>
public class AccountProcessRowPainter(AccountGridProcessExpander expander)
{
    private DataGridView _grid = null!;
    private IGridSortState _sortState = null!;
    private bool _hasProcessService;

    // null = not loaded, _iconLoadingPlaceholder = loading in progress, any other Image = ready
    private static readonly Image _iconLoadingPlaceholder = new Bitmap(1, 1);
    private readonly Dictionary<string, Image?> _processIconCache = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(DataGridView grid, IGridSortState sortState, bool hasProcessService)
    {
        _grid = grid;
        _sortState = sortState;
        _hasProcessService = hasProcessService;
    }

    public void Paint(DataGridViewCellPaintingEventArgs e)
    {
        AccountGridHelper.PaintSidCell(_grid, e);
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ProcessRow)
            AccountGridHelper.PaintSidCell(_grid, e, "ProfilePath");
        PaintProcessToggleGlyph(e);
        PaintProcessTreeLines(e);
        SuppressProcessRowCellContent(e);
    }

    public void PostPaint(DataGridViewRowPostPaintEventArgs e)
    {
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not ProcessRow processRow)
            return;

        var accountColRect = _grid.GetColumnDisplayRectangle(_grid.Columns["Account"]!.Index, false);
        const int iconSize = 22;
        const int iconGap = 3;
        int iconX = accountColRect.X + 20;
        int textStartX = iconX + iconSize + iconGap;
        int textWidth = _grid.ClientRectangle.Right - textStartX;
        if (textWidth <= 0)
            return;

        bool isSelected = (e.State & DataGridViewElementStates.Selected) != 0;
        var textColor = isSelected
            ? row.DefaultCellStyle.SelectionForeColor
            : row.DefaultCellStyle.ForeColor != Color.Empty
                ? row.DefaultCellStyle.ForeColor
                : SystemColors.ControlText;

        var exePath = processRow.Process.ExecutablePath;
        if (exePath != null)
        {
            if (!_processIconCache.TryGetValue(exePath, out var icon))
            {
                _processIconCache[exePath] = _iconLoadingPlaceholder;
                string capturedPath = exePath;
                Task.Run(() => ShortcutIconHelper.ExtractIcon(capturedPath, iconSize)).ContinueWith(t =>
                {
                    _processIconCache[capturedPath] = t.IsFaulted ? null : t.Result;
                    if (_grid.IsDisposed)
                        return;
                    foreach (DataGridViewRow r in _grid.Rows)
                    {
                        if (r.Tag is ProcessRow { Process.ExecutablePath: not null } pr &&
                            string.Equals(pr.Process.ExecutablePath, capturedPath, StringComparison.OrdinalIgnoreCase))
                            _grid.InvalidateRow(r.Index);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else if (!ReferenceEquals(icon, _iconLoadingPlaceholder) && icon != null)
            {
                int iconY = e.RowBounds.Y + (e.RowBounds.Height - iconSize) / 2;
                e.Graphics.DrawImage(icon, iconX, iconY, iconSize, iconSize);
            }
        }

        // Draw PID right-aligned in a fixed-width column, then exe+args left-aligned after it.
        string pidStr = processRow.Process.Pid.ToString();
        int pidColWidth = TextRenderer.MeasureText(e.Graphics, new string('0', processRow.PidColumnChars),
            _grid.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        string exeAndArgs = processRow.DisplayLine.Length > pidStr.Length
            ? processRow.DisplayLine[(pidStr.Length + 1)..] // skip "PID "
            : processRow.DisplayLine;

        var pidBounds = e.RowBounds with { X = textStartX, Width = pidColWidth };
        TextRenderer.DrawText(e.Graphics, pidStr, _grid.Font, pidBounds, textColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int exeX = textStartX + pidColWidth + 4;
        int exeWidth = _grid.ClientRectangle.Right - exeX;
        if (exeWidth > 0)
        {
            var exeBounds = e.RowBounds with { X = exeX, Width = exeWidth };
            TextRenderer.DrawText(e.Graphics, exeAndArgs, _grid.Font, exeBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private void PaintProcessToggleGlyph(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_grid.Columns[e.ColumnIndex].Name != "Account")
            return;

        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not (AccountRow or ContainerRow))
            return;

        string? sid = AccountGridProcessExpander.GetSidFromRow(row);

        e.PaintBackground(e.ClipBounds, e.State.HasFlag(DataGridViewElementStates.Selected));

        bool isSelected = e.State.HasFlag(DataGridViewElementStates.Selected);
        var bounds = e.CellBounds;

        if (_hasProcessService && !_sortState.IsSortActive && !string.IsNullOrEmpty(sid) && expander.HasProcesses(sid))
        {
            // Draw Windows TreeView-style +/- box
            bool expanded = expander.IsExpanded(sid);
            const int boxSize = 13;
            int boxX = bounds.X + 3;
            int boxY = bounds.Y + (bounds.Height - boxSize) / 2;
            using var boxPen = new Pen(Color.Gray);
            e.Graphics!.DrawRectangle(boxPen, boxX, boxY, boxSize, boxSize);
            int cx = boxX + boxSize / 2;
            int cy = boxY + boxSize / 2;
            using var linePen = new Pen(Color.DimGray);
            e.Graphics.DrawLine(linePen, boxX + 3, cy, boxX + boxSize - 3, cy);
            if (!expanded)
                e.Graphics.DrawLine(linePen, cx, boxY + 3, cx, boxY + boxSize - 3);
        }

        // Draw credential icon inline between toggle glyph area and account name
        const int toggleWidth = 20;
        const int iconSize = 16;
        const int iconGap = 2;
        const int textOffset = toggleWidth + iconSize + iconGap;

        if (row.Cells["Credential"].Value is Image { Width: > 1 } credImage)
        {
            int iconY = bounds.Y + (bounds.Height - iconSize) / 2;
            e.Graphics!.DrawImage(credImage, bounds.X + toggleWidth, iconY, iconSize, iconSize);
        }

        // All account/container rows always render text at the same offset
        var baseFont = e.CellStyle?.Font ?? _grid.Font;
        var textColor = isSelected
            ? e.CellStyle?.SelectionForeColor ?? SystemColors.HighlightText
            : e.CellStyle?.ForeColor ?? SystemColors.ControlText;
        var textBounds = bounds with { X = bounds.X + textOffset, Width = bounds.Width - textOffset };
        TextRenderer.DrawText(e.Graphics!, e.Value?.ToString() ?? "", baseFont, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        e.Handled = true;
    }

    private void PaintProcessTreeLines(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_grid.Columns[e.ColumnIndex].Name != "Account")
            return;
        if (_grid.Rows[e.RowIndex].Tag is not ProcessRow processRow)
            return;

        e.PaintBackground(e.ClipBounds, e.State.HasFlag(DataGridViewElementStates.Selected));

        var bounds = e.CellBounds;

        // Vertical rail aligns with the center of the toggle box (boxX=3, BoxSize=13 → center=9)
        const int railX = 9;
        int lineX = bounds.X + railX;
        int cy = bounds.Y + bounds.Height / 2;

        using var treePen = new Pen(Color.Silver);
        e.Graphics!.DrawLine(treePen, lineX, bounds.Y, lineX, processRow.IsLast ? cy : bounds.Y + bounds.Height);
        e.Graphics.DrawLine(treePen, lineX, cy, bounds.X + 18, cy);

        // Text is drawn in PostPaint to span across all columns
        e.Handled = true;
    }

    public string? GetProcessRowTooltip(ProcessRow processRow)
    {
        if (!_grid.Columns.Contains("Account"))
            return null;
        var accountColRect = _grid.GetColumnDisplayRectangle(_grid.Columns["Account"]!.Index, false);
        const int iconSize = 22;
        const int iconGap = 3;
        int iconX = accountColRect.X + 20;
        int textStartX = iconX + iconSize + iconGap;

        string pidStr = processRow.Process.Pid.ToString();
        int pidColWidth = TextRenderer.MeasureText(new string('0', processRow.PidColumnChars),
            _grid.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        string exeAndArgs = processRow.DisplayLine.Length > pidStr.Length
            ? processRow.DisplayLine[(pidStr.Length + 1)..]
            : processRow.DisplayLine;

        int exeX = textStartX + pidColWidth + 4;
        int exeWidth = _grid.ClientRectangle.Right - exeX;
        if (exeWidth <= 0)
            return processRow.DisplayLine.ReplaceLineEndings(" ").TrimEnd();

        int textWidth = TextRenderer.MeasureText(exeAndArgs, _grid.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        return textWidth > exeWidth ? processRow.DisplayLine.ReplaceLineEndings(" ").TrimEnd() : null;
    }

    private void SuppressProcessRowCellContent(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_grid.Columns[e.ColumnIndex].Name == "Account")
            return; // handled by PaintProcessTreeLines
        if (_grid.Rows[e.RowIndex].Tag is not ProcessRow)
            return;
        e.PaintBackground(e.ClipBounds, e.State.HasFlag(DataGridViewElementStates.Selected));
        e.Handled = true;
    }
}
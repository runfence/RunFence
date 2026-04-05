using System.Drawing.Drawing2D;
using System.Drawing.Text;
using RunFence.UI;

namespace RunFence.Account.UI;

public static class AccountGridHelper
{
    public static readonly Bitmap EmptyIcon = new(1, 1);

    private static Font? _boldFont;
    private static Font? _boldFontBase;

    public static Image CreateKeyIcon()
    {
        return UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0xB8, 0x8A, 0x00), 16);
    }

    public static Image CreateContainerIcon()
    {
        return UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 16);
    }

    public static Image CreateWarningBadgeIcon()
    {
        var bmp = new Bitmap(24, 24);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI Symbol", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(0xCC, 0x88, 0x00));
        using var fmt = new StringFormat();
        fmt.Alignment = StringAlignment.Center;
        fmt.LineAlignment = StringAlignment.Center;
        g.DrawString("\u26A0", font, brush, new RectangleF(0, 0, 24, 24), fmt);
        return bmp;
    }

    public static Image CreateUserProfileIcon(int size = 28)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var color = Color.FromArgb(0x33, 0x66, 0x99);

        // Draw head (circle)
        float headRadius = size * 0.18f;
        float headX = size * 0.5f;
        float headY = size * 0.3f;
        using var headBrush = new SolidBrush(color);
        g.FillEllipse(headBrush, headX - headRadius, headY - headRadius, headRadius * 2, headRadius * 2);

        // Draw shoulders/body (trapezoid)
        float bodyTop = headY + headRadius + size * 0.05f;
        float bodyHeight = size * 0.35f;
        float bodyLeft = size * 0.25f;
        float bodyRight = size * 0.75f;
        float bodyBottomLeft = size * 0.15f;
        float bodyBottomRight = size * 0.85f;

        var bodyPoints = new[]
        {
            new PointF(bodyLeft, bodyTop),
            new PointF(bodyRight, bodyTop),
            new PointF(bodyBottomRight, bodyTop + bodyHeight),
            new PointF(bodyBottomLeft, bodyTop + bodyHeight),
        };
        g.FillPolygon(headBrush, bodyPoints);

        return bmp;
    }
    
    public static void PaintSidCell(DataGridView grid, DataGridViewCellPaintingEventArgs e, string columnName = "SID")
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (grid.Columns[e.ColumnIndex].Name != columnName)
            return;
        if (e.Value is not string text || string.IsNullOrEmpty(text))
            return;

        e.PaintBackground(e.ClipBounds, e.State.HasFlag(DataGridViewElementStates.Selected));

        var font = e.CellStyle?.Font ?? grid.DefaultCellStyle.Font ?? grid.Font;
        var color = e.State.HasFlag(DataGridViewElementStates.Selected)
            ? e.CellStyle?.SelectionForeColor ?? SystemColors.HighlightText
            : e.CellStyle?.ForeColor ?? SystemColors.ControlText;

        var bounds = e.CellBounds;
        var padding = e.CellStyle?.Padding ?? Padding.Empty;
        var contentBounds = new Rectangle(
            bounds.X + padding.Left + 2, bounds.Y + padding.Top,
            bounds.Width - padding.Horizontal - 4, bounds.Height - padding.Vertical);

        var fullSize = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        if (fullSize.Width <= contentBounds.Width)
        {
            TextRenderer.DrawText(e.Graphics!, text, font, contentBounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            const string ellipsis = "...";
            var displayText = ellipsis;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                var candidate = ellipsis + text[i..];
                var w = TextRenderer.MeasureText(candidate, font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (w > contentBounds.Width)
                    break;
                displayText = candidate;
            }

            TextRenderer.DrawText(e.Graphics!, displayText, font, contentBounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        e.Handled = true;
    }

    private static Font GetOrCreateBoldFont(Font baseFont)
    {
        if (_boldFont != null && ReferenceEquals(_boldFontBase, baseFont))
            return _boldFont;
        _boldFont?.Dispose();
        _boldFont = new Font(baseFont, FontStyle.Bold);
        _boldFontBase = baseFont;
        return _boldFont;
    }

    public static void AddGroupHeaderRow(DataGridView grid, string title)
    {
        var colCount = grid.Columns.Count;
        var values = new object[colCount];
        values[grid.Columns["Import"]!.Index] = false;
        values[grid.Columns["Credential"]!.Index] = EmptyIcon;
        values[grid.Columns["Account"]!.Index] = title;
        values[grid.Columns["Logon"]!.Index] = false;
        values[grid.Columns["Apps"]!.Index] = "";
        values[grid.Columns["ProfilePath"]!.Index] = "";
        values[grid.Columns["SID"]!.Index] = "";
        var idx = grid.Rows.Add(values);
        var row = grid.Rows[idx];
        row.Tag = new AccountGroupHeader();
        row.DefaultCellStyle.BackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        row.DefaultCellStyle.Font = GetOrCreateBoldFont(grid.Font);
        row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        row.DefaultCellStyle.SelectionForeColor = Color.Black;
        row.Height = 22;
        foreach (DataGridViewCell cell in row.Cells)
            cell.ReadOnly = true;
        foreach (var colName in new[] { "Import", "Logon", "colAllowInternet" })
        {
            var colIndex = grid.Columns[colName]?.Index;
            if (colIndex.HasValue)
                row.Cells[colIndex.Value] = new DataGridViewTextBoxCell { Value = "" };
        }
    }
}
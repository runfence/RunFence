namespace RunFence.UI.Controls;

public class StyledDataGridView : DataGridView
{
    private Font? _headerBoldFont;

    public StyledDataGridView()
    {
        EnableHeadersVisualStyles = false;
        ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0);
        _headerBoldFont = new Font(Font, FontStyle.Bold);
        ColumnHeadersDefaultCellStyle.Font = _headerBoldFont;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(0xF0, 0xF0, 0xF0);
        ColumnHeadersDefaultCellStyle.SelectionForeColor = ColumnHeadersDefaultCellStyle.ForeColor;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(0xF7, 0xF9, 0xFC);
        DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xCC, 0xE5, 0xFF);
        DefaultCellStyle.SelectionForeColor = Color.Black;
        GridColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        RowHeadersVisible = false;
        AllowUserToResizeRows = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        BorderStyle = BorderStyle.None;
        BackgroundColor = SystemColors.Window;
        RowTemplate.Height = 26;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        var old = _headerBoldFont;
        _headerBoldFont = new Font(Font, FontStyle.Bold);
        ColumnHeadersDefaultCellStyle.Font = _headerBoldFont;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerBoldFont?.Dispose();
            _headerBoldFont = null;
        }

        base.Dispose(disposing);
    }

    public bool ShouldSerializeBackgroundColor() => BackgroundColor != SystemColors.Window;
    public void ResetBackgroundColor() => BackgroundColor = SystemColors.Window;

    public bool ShouldSerializeGridColor() => GridColor != Color.FromArgb(0xE0, 0xE0, 0xE0);
    public void ResetGridColor() => GridColor = Color.FromArgb(0xE0, 0xE0, 0xE0);

    public bool ShouldSerializeBorderStyle() => BorderStyle != BorderStyle.None;
    public void ResetBorderStyle() => BorderStyle = BorderStyle.None;

    public bool ShouldSerializeRowHeadersVisible() => RowHeadersVisible;
    public void ResetRowHeadersVisible() => RowHeadersVisible = false;

    public bool ShouldSerializeAllowUserToResizeRows() => AllowUserToResizeRows;
    public void ResetAllowUserToResizeRows() => AllowUserToResizeRows = false;

    public bool ShouldSerializeColumnHeadersHeightSizeMode()
        => ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

    public void ResetColumnHeadersHeightSizeMode()
        => ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

    public bool ShouldSerializeCellBorderStyle() => CellBorderStyle != DataGridViewCellBorderStyle.SingleHorizontal;
    public void ResetCellBorderStyle() => CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
}
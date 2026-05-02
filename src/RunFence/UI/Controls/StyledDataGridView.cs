// ReSharper disable UnusedMember.Global
using System.ComponentModel;

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
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
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

    // Property overrides to redefine designer defaults to match StyledDataGridView's defaults.
    //
    // Bool/enum properties: [DefaultValue(X)] tells the designer the new default so it only
    // serializes when the value has been explicitly changed from X on a specific instance.
    //
    // BackgroundColor: uses the type/string DefaultValue form so the ColorConverter can resolve
    // the system color name "Window" to SystemColors.Window at design time.
    //
    // GridColor: uses ShouldSerialize (non-constant ARGB default can't be expressed in [DefaultValue]).
    // The 'new' property ensures TypeDescriptor builds a descriptor rooted at StyledDataGridView,
    // picking up ShouldSerializeGridColor() from this class rather than any base-class logic.
    //
    // Cell style properties (ColumnHeadersDefaultCellStyle, DefaultCellStyle,
    // AlternatingRowsDefaultCellStyle): [DesignerSerializationVisibility(Hidden)] is correct here.
    // DataGridView marks these as [DesignerSerializationVisibility(Content)], meaning the designer
    // serializes each sub-property individually and bypasses ShouldSerialize/DefaultValue entirely.
    // Cell styles are always configured in code; they are never meaningfully edited via the PropertyGrid.

    [DefaultValue(false)]
    public new bool AllowUserToAddRows { get => base.AllowUserToAddRows; set => base.AllowUserToAddRows = value; }

    [DefaultValue(false)]
    public new bool AllowUserToDeleteRows { get => base.AllowUserToDeleteRows; set => base.AllowUserToDeleteRows = value; }

    [DefaultValue(false)]
    public new bool AllowUserToResizeRows { get => base.AllowUserToResizeRows; set => base.AllowUserToResizeRows = value; }

    [DefaultValue(DataGridViewSelectionMode.FullRowSelect)]
    public new DataGridViewSelectionMode SelectionMode { get => base.SelectionMode; set => base.SelectionMode = value; }

    [DefaultValue(false)]
    public new bool MultiSelect { get => base.MultiSelect; set => base.MultiSelect = value; }

    [DefaultValue(false)]
    public new bool RowHeadersVisible { get => base.RowHeadersVisible; set => base.RowHeadersVisible = value; }

    [DefaultValue(typeof(Color), "Window")]
    public new Color BackgroundColor { get => base.BackgroundColor; set => base.BackgroundColor = value; }

    public new Color GridColor { get => base.GridColor; set => base.GridColor = value; }
    public bool ShouldSerializeGridColor() => GridColor != Color.FromArgb(0xE0, 0xE0, 0xE0);
    public void ResetGridColor() => GridColor = Color.FromArgb(0xE0, 0xE0, 0xE0);

    [DefaultValue(false)]
    public new bool EnableHeadersVisualStyles { get => base.EnableHeadersVisualStyles; set => base.EnableHeadersVisualStyles = value; }

    [DefaultValue(DataGridViewCellBorderStyle.SingleHorizontal)]
    public new DataGridViewCellBorderStyle CellBorderStyle { get => base.CellBorderStyle; set => base.CellBorderStyle = value; }

    [DefaultValue(DataGridViewHeaderBorderStyle.Single)]
    public new DataGridViewHeaderBorderStyle ColumnHeadersBorderStyle { get => base.ColumnHeadersBorderStyle; set => base.ColumnHeadersBorderStyle = value; }

    [DefaultValue(DataGridViewColumnHeadersHeightSizeMode.DisableResizing)]
    public new DataGridViewColumnHeadersHeightSizeMode ColumnHeadersHeightSizeMode { get => base.ColumnHeadersHeightSizeMode; set => base.ColumnHeadersHeightSizeMode = value; }

    [DefaultValue(BorderStyle.None)]
    public new BorderStyle BorderStyle { get => base.BorderStyle; set => base.BorderStyle = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new DataGridViewCellStyle ColumnHeadersDefaultCellStyle { get => base.ColumnHeadersDefaultCellStyle; set => base.ColumnHeadersDefaultCellStyle = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new DataGridViewCellStyle DefaultCellStyle { get => base.DefaultCellStyle; set => base.DefaultCellStyle = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new DataGridViewCellStyle AlternatingRowsDefaultCellStyle { get => base.AlternatingRowsDefaultCellStyle; set => base.AlternatingRowsDefaultCellStyle = value; }
}

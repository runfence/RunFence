namespace RunFence.Acl.UI;

public sealed class AclManagerSectionHeaderFactory : IDisposable
{
    public static readonly Color SectionHeaderBackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
    private readonly Dictionary<DataGridView, Font> _boldFontCache = new(ReferenceEqualityComparer.Instance);

    public DataGridViewRow CreateSectionHeaderRow(DataGridView grid, string sectionTitle, string? configPath, int titleCellIndex)
    {
        var headerRow = new DataGridViewRow();
        headerRow.CreateCells(grid);

        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (headerRow.Cells[i] is DataGridViewCheckBoxCell or DataGridViewComboBoxCell or DataGridViewImageCell)
                headerRow.Cells[i] = new DataGridViewTextBoxCell();
        }

        headerRow.Cells[titleCellIndex].Value = sectionTitle;
        headerRow.DefaultCellStyle.BackColor = SectionHeaderBackColor;
        headerRow.DefaultCellStyle.ForeColor = Color.Black;
        headerRow.DefaultCellStyle.SelectionBackColor = SectionHeaderBackColor;
        headerRow.DefaultCellStyle.SelectionForeColor = Color.Black;
        headerRow.DefaultCellStyle.Font = GetOrCreateBoldFont(grid);
        headerRow.ReadOnly = true;
        headerRow.Tag = new ConfigSectionHeader(configPath);
        return headerRow;
    }

    public void Dispose()
    {
        foreach (var font in _boldFontCache.Values)
            font.Dispose();
        _boldFontCache.Clear();
    }

    private Font GetOrCreateBoldFont(DataGridView grid)
    {
        if (_boldFontCache.TryGetValue(grid, out var cached))
            return cached;

        var bold = new Font(grid.Font, FontStyle.Bold);
        _boldFontCache[grid] = bold;
        grid.FontChanged += (_, _) =>
        {
            if (_boldFontCache.Remove(grid, out var old))
                old.Dispose();
            _boldFontCache[grid] = new Font(grid.Font, FontStyle.Bold);
        };
        grid.Disposed += (_, _) =>
        {
            if (_boldFontCache.Remove(grid, out var font))
                font.Dispose();
        };
        return bold;
    }
}

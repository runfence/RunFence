namespace RunFence.Account.UI;

public class AccountGridRowComposer(AccountGridIconLifetimeManager iconLifetimeManager) : IDisposable
{
    private readonly Dictionary<DataGridView, Font> _boldFontCache = new(ReferenceEqualityComparer.Instance);

    public DataGridViewRow AddAccountGridRow(
        DataGridView grid,
        AccountRow accountRow,
        Image icon,
        string displayName,
        bool logonValue,
        bool allowInternet,
        string appsText,
        string profilePath)
    {
        var rowIndex = grid.Rows.Add(
            false,
            icon,
            displayName,
            logonValue,
            allowInternet,
            appsText,
            profilePath,
            accountRow.Sid);
        var row = grid.Rows[rowIndex];
        iconLifetimeManager.TrackOwned(row, icon);
        row.Tag = accountRow;
        row.Cells[AccountGridColumns.Sid].ToolTipText = accountRow.Sid;
        return row;
    }

    public DataGridViewRow AddGroupHeaderRow(DataGridView grid, string title)
    {
        var values = new object[grid.Columns.Count];
        values[grid.Columns[AccountGridColumns.Import]!.Index] = false;
        values[grid.Columns[AccountGridColumns.Credential]!.Index] = AccountGridHelper.EmptyIcon;
        values[grid.Columns[AccountGridColumns.Account]!.Index] = title;
        values[grid.Columns[AccountGridColumns.Logon]!.Index] = false;
        values[grid.Columns[AccountGridColumns.Apps]!.Index] = "";
        values[grid.Columns[AccountGridColumns.ProfilePath]!.Index] = "";
        values[grid.Columns[AccountGridColumns.Sid]!.Index] = "";

        var rowIndex = grid.Rows.Add(values);
        var row = grid.Rows[rowIndex];
        row.Tag = new AccountGroupHeader();
        row.DefaultCellStyle.BackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        row.DefaultCellStyle.Font = GetOrCreateBoldFont(grid);
        row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);
        row.DefaultCellStyle.SelectionForeColor = Color.Black;
        row.Height = 22;

        foreach (DataGridViewCell cell in row.Cells)
            cell.ReadOnly = true;

        foreach (var columnName in new[] { AccountGridColumns.Import, AccountGridColumns.Logon, AccountGridColumns.AllowInternet })
        {
            var columnIndex = grid.Columns[columnName]?.Index;
            if (columnIndex.HasValue)
                row.Cells[columnIndex.Value] = new DataGridViewTextBoxCell { Value = "" };
        }

        return row;
    }

    public DataGridViewRow AddAppContainerGridRow(
        DataGridView grid,
        ContainerRow containerRow,
        Image icon,
        string displayName,
        string appsText,
        string profilePath)
    {
        var rowIndex = grid.Rows.Add(
            false,
            icon,
            displayName,
            true,
            true,
            appsText,
            profilePath,
            containerRow.ContainerSid);
        var row = grid.Rows[rowIndex];
        iconLifetimeManager.TrackOwned(row, icon);
        row.Tag = containerRow;
        row.Cells[AccountGridColumns.Sid].ToolTipText = containerRow.ContainerSid;
        return row;
    }

    public void Dispose()
    {
        foreach (var grid in _boldFontCache.Keys.ToArray())
        {
            grid.FontChanged -= OnGridFontChanged;
            grid.Disposed -= OnGridDisposed;
        }

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
        grid.FontChanged += OnGridFontChanged;
        grid.Disposed += OnGridDisposed;
        return bold;
    }

    private void OnGridFontChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGridView grid)
            return;

        if (!_boldFontCache.TryGetValue(grid, out var oldFont))
            return;

        var newFont = new Font(grid.Font, FontStyle.Bold);
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is AccountGroupHeader)
                row.DefaultCellStyle.Font = newFont;
        }

        _boldFontCache[grid] = newFont;
        oldFont.Dispose();
    }

    private void OnGridDisposed(object? sender, EventArgs e)
    {
        if (sender is not DataGridView grid)
            return;

        grid.FontChanged -= OnGridFontChanged;
        grid.Disposed -= OnGridDisposed;

        if (_boldFontCache.Remove(grid, out var font))
            font.Dispose();
    }
}

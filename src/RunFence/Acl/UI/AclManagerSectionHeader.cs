using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Row tag for section header rows in the grants and traverse grids.
/// </summary>
public record struct ConfigSectionHeader(string? ConfigPath);

/// <summary>
/// Shared factory for section-header rows used in the grants and traverse grids of
/// <see cref="AclManagerDialog"/>.
/// </summary>
public static class AclManagerSectionHeader
{
    /// <summary>Background color used for section header rows (also used when clearing drag-drop highlight).</summary>
    public static readonly Color SectionHeaderBackColor = Color.FromArgb(0xE4, 0xEA, 0xF4);

    private static readonly Dictionary<DataGridView, Font> _boldFontCache = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the config path of the section that contains the given row index,
    /// by walking backwards to find the nearest section header. Returns null for main config
    /// or when no section header is found.
    /// </summary>
    public static string? GetSectionConfigPath(DataGridView grid, int rowIndex)
    {
        for (int i = rowIndex; i >= 0; i--)
        {
            if (grid.Rows[i].Tag is ConfigSectionHeader header)
                return header.ConfigPath;
        }

        return null;
    }

    /// <summary>
    /// Returns all data rows effectively selected, expanding section-header selections to include
    /// all data rows under that section. Deduplicates so each row appears at most once.
    /// </summary>
    public static List<DataGridViewRow> ExpandSectionSelection(DataGridView grid)
    {
        var selected = grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        var result = new HashSet<DataGridViewRow>(ReferenceEqualityComparer.Instance);

        foreach (var row in selected)
        {
            switch (row.Tag)
            {
                case GrantedPathEntry:
                    result.Add(row);
                    break;
                case ConfigSectionHeader:
                {
                    // Expand: add all data rows from this header to the next header (or end)
                    bool inSection = false;
                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        var r = grid.Rows[i];
                        if (ReferenceEquals(r, row))
                        {
                            inSection = true;
                            continue;
                        }

                        if (!inSection)
                            continue;
                        if (r.Tag is ConfigSectionHeader)
                            break;
                        if (r.Tag is GrantedPathEntry)
                            result.Add(r);
                    }

                    break;
                }
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// Creates a styled, read-only header row for a config section.
    /// <paramref name="sectionTitle"/> is placed in cell at <paramref name="titleCellIndex"/> (default 0);
    /// other cells are empty. Image/checkbox/combobox cells are replaced with plain text cells.
    /// The row's <c>Tag</c> is set to <see cref="ConfigSectionHeader"/>(<paramref name="configPath"/>).
    /// </summary>
    public static DataGridViewRow CreateSectionHeaderRow(DataGridView grid, string sectionTitle,
        string? configPath, int titleCellIndex = 0)
    {
        var headerRow = new DataGridViewRow();
        headerRow.CreateCells(grid);

        // Replace non-text cells (checkbox, combobox, image) with text cells for clean header rendering
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

    private static Font GetOrCreateBoldFont(DataGridView grid)
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
            if (_boldFontCache.Remove(grid, out var f))
                f.Dispose();
        };
        return bold;
    }
}
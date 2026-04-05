using RunFence.Acl.UI.Forms;

namespace RunFence.Acl.UI;

/// <summary>
/// Static grid-column builders for <see cref="AclManagerDialog"/>.
/// Keeps column definitions separate from dialog event-handling logic.
/// </summary>
public static class AclManagerDialogGridSetup
{
    /// <summary>Populates the grants grid with icon, path, mode, rights, and optional Own columns.</summary>
    public static void BuildGrantsGrid(DataGridView grid, bool isContainer)
    {
        grid.Columns.Clear();

        var colIcon = new DataGridViewImageColumn
        {
            Name = AclManagerGrantsHelper.ColIcon, HeaderText = "", Width = 20,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colIcon.DefaultCellStyle.NullValue = null;
        colIcon.DefaultCellStyle.Padding = new Padding(2);

        var colPath = new DataGridViewTextBoxColumn
        {
            Name = AclManagerGrantsHelper.ColPath, HeaderText = "Path", ReadOnly = true, Width = 340,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        };

        var colMode = new DataGridViewComboBoxColumn
        {
            Name = AclManagerGrantsHelper.ColMode, HeaderText = "Mode", Width = 75, ReadOnly = false,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colMode.Items.AddRange(AclManagerGrantsHelper.ModeAllow, AclManagerGrantsHelper.ModeDeny);

        var colRead = MakeCheckbox(AclManagerGrantsHelper.ColRead, "Read", 50);
        var colExecute = MakeCheckbox(AclManagerGrantsHelper.ColExecute, "Execute", 60);
        var colWrite = MakeCheckbox(AclManagerGrantsHelper.ColWrite, "Write", 50);
        var colSpecial = MakeCheckbox(AclManagerGrantsHelper.ColSpecial, "Special", 60);

        grid.Columns.AddRange(colIcon, colPath, colMode, colRead, colExecute, colWrite, colSpecial);

        if (!isContainer)
        {
            var colOwner = MakeCheckbox(AclManagerGrantsHelper.ColOwner, "Owner", 50);
            grid.Columns.Add(colOwner);
        }
    }

    /// <summary>Populates the traverse grid with icon and path columns.</summary>
    public static void BuildTraverseGrid(DataGridView grid)
    {
        grid.Columns.Clear();

        var colIcon = new DataGridViewImageColumn
        {
            Name = AclManagerGrantsHelper.ColIcon, HeaderText = "", Width = 20,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
        };
        colIcon.DefaultCellStyle.NullValue = null;
        colIcon.DefaultCellStyle.Padding = new Padding(2);

        grid.Columns.AddRange(colIcon, new DataGridViewTextBoxColumn
        {
            Name = "TraversePath", HeaderText = "Path", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
    }

    private static DataGridViewCheckBoxColumn MakeCheckbox(string name, string header, int width)
        => new()
        {
            Name = name, HeaderText = header, Width = width,
            ThreeState = false,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            TrueValue = CheckState.Checked,
            FalseValue = CheckState.Unchecked,
            IndeterminateValue = CheckState.Indeterminate,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
}
using RunFence.UI;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Dialog for selecting interactive user associations to import as direct handler mappings.
/// </summary>
public class ImportAssociationsDialog : Form
{
    private StyledDataGridView _grid = null!;
    private DataGridViewCheckBoxColumn _colCheck = null!;
    private DataGridViewTextBoxColumn _colKey = null!;
    private DataGridViewTextBoxColumn _colHandler = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    private Button _selectAllButton = null!;
    private Button _deselectAllButton = null!;
    private IReadOnlyList<InteractiveAssociationEntry> _entries = [];
    private readonly GridSortHelper _sortHelper = new();

    public ImportAssociationsDialog()
    {
        BuildControls();
    }

    private void BuildControls()
    {
        Text = "Import Associations";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 400);
        MinimumSize = new Size(360, 300);

        _colCheck = new DataGridViewCheckBoxColumn
        {
            HeaderText = string.Empty,
            Name = "colCheck",
            Width = 30,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _colKey = new DataGridViewTextBoxColumn
        {
            HeaderText = "Association",
            Name = "colKey",
            Width = 100,
            ReadOnly = true
        };
        _colHandler = new DataGridViewTextBoxColumn
        {
            HeaderText = "Handler",
            Name = "colHandler",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        };

        _grid = new StyledDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true
        };
        _grid.Columns.AddRange(_colCheck, _colKey, _colHandler);
        _grid.CellContentClick += OnGridCellContentClick;
        _sortHelper.EnableThreeStateSorting(_grid, PopulateGrid);

        // Button panel: left side has Select All/Deselect All, right side has OK/Cancel.
        // Padding (0, 6, 6, 6) makes inner height 40 - 6 - 6 = 28px so Dock=Right buttons are 28px tall.
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(0, 6, 6, 6)
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };
        _okButton = new Button
        {
            Text = "Import",
            DialogResult = DialogResult.OK,
            Width = 80,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };

        _selectAllButton = new Button
        {
            Text = "Select All",
            Location = new Point(6, 6),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.System
        };
        _deselectAllButton = new Button
        {
            Text = "Deselect All",
            Location = new Point(102, 6),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.System
        };

        _selectAllButton.Click += (_, _) => SetAllChecked(true);
        _deselectAllButton.Click += (_, _) => SetAllChecked(false);

        // Dock=Right buttons must be added before left-aligned buttons so they fill the right edge.
        // First added = rightmost; cancel added first → rightmost, then ok to its left.
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(_selectAllButton);
        buttonPanel.Controls.Add(_deselectAllButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.Add(_grid);
        Controls.Add(buttonPanel);

        Icon = AppIcons.GetAppIcon();
    }

    /// <summary>
    /// Populates the dialog with entries to choose from. Must be called before ShowDialog.
    /// </summary>
    public void Initialize(IReadOnlyList<InteractiveAssociationEntry> entries, IReadOnlySet<string> existingKeys)
    {
        _entries = entries.Where(e => !existingKeys.Contains(e.Key)).ToList();
        PopulateGrid();
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var entry in _entries)
        {
            var rowIndex = _grid.Rows.Add(true, entry.Key, entry.Description);
            _grid.Rows[rowIndex].Tag = entry;
        }
    }

    private void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == _colCheck.Index)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void SetAllChecked(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells[_colCheck.Index] is DataGridViewCheckBoxCell cell)
                cell.Value = value;
        }
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    /// <summary>
    /// Returns the checked entries after the dialog is accepted.
    /// </summary>
    public IReadOnlyList<InteractiveAssociationEntry> SelectedEntries
    {
        get
        {
            var result = new List<InteractiveAssociationEntry>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is InteractiveAssociationEntry entry &&
                    row.Cells[_colCheck.Index].Value is true)
                {
                    result.Add(entry);
                }
            }
            return result;
        }
    }
}

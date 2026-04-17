using System.ComponentModel;
using System.Security.Principal;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SidMigration.UI.Forms;
using RunFence.UI;

namespace RunFence.SidMigration.UI;

/// <summary>
/// Builds and manages the SID mapping grid for <see cref="MigrationMappingStep"/>.
/// Handles grid population, combo box formatting, and collecting migration/delete selections.
/// Data/SID resolution is delegated to <see cref="SidMigrationMappingLogic"/>.
/// </summary>
public class SidMigrationMappingBuilder(
    SidMigrationMappingLogic logic,
    ILoggingService log,
    IEnumerable<OrphanedSid> orphanedSids)
{
    private Dictionary<string, string> _sidDisplayNames = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Ready;
    public event Action? Failed;

    /// <summary>
    /// The populated content panel. Set before <see cref="Ready"/> fires; null until then.
    /// The caller is responsible for adding this control to its own layout.
    /// </summary>
    public Control? Content { get; private set; }

    private DataGridView? MappingGrid { get; set; }

    public async Task BuildMappingsAsync(Label loadingLabel)
    {
        Dictionary<string, (string guessedName, string? newSid)> allMappings;

        try
        {
            allMappings = await logic.BuildMappingsAsync();
        }
        catch (Exception ex)
        {
            log.Error("Failed to build SID mappings", ex);
            if (!loadingLabel.IsDisposed)
            {
                loadingLabel.Text = $"Error building mappings: {ex.Message}";
                Failed?.Invoke();
            }

            return;
        }

        if (loadingLabel.IsDisposed)
            return;

        loadingLabel.Visible = false;
        Content = BuildContentPanel(allMappings);
        Ready?.Invoke();
    }

    private Panel BuildContentPanel(Dictionary<string, (string guessedName, string? newSid)> allMappings)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        PopulatePanel(panel, allMappings);
        return panel;
    }

    private void PopulatePanel(Panel panel, Dictionary<string, (string guessedName, string? newSid)> allMappings)
    {
        _sidDisplayNames = logic.BuildSidDisplayNames();

        var orphanedLookup = orphanedSids.ToDictionary(o => o.Sid, StringComparer.OrdinalIgnoreCase);

        MappingGrid = new DataGridView
        {
            Location = new Point(15, 10),
            Size = new Size(560, 180),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AllowUserToResizeRows = false
        };

        MappingGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Action", HeaderText = "Action", FillWeight = 12,
            Items = { "Skip", "Migrate", "Delete" }, FlatStyle = FlatStyle.Flat
        });
        MappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 20 });
        MappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OldSid", HeaderText = "Old SID", FillWeight = 30 });

        var newSidCol = new DataGridViewComboBoxColumn
        {
            Name = "NewSid", HeaderText = "New SID", FillWeight = 30,
            FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
        };
        foreach (var sid in logic.GetLocalUserSids())
            newSidCol.Items.Add(sid);
        MappingGrid.Columns.Add(newSidCol);

        MappingGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (MappingGrid.IsCurrentCellDirty)
                MappingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        // Open drop-down on first click for the Action column only;
        // NewSid uses DropDown (editable) style so auto-open would be disruptive.
        DataGridViewComboHelper.EnableComboOpenOnFirstClick(MappingGrid,
            col => MappingGrid.Columns[col].Name == "Action");
        MappingGrid.CellValueChanged += OnMappingGridCellValueChanged;
        MappingGrid.CellPainting += OnMappingGridCellPainting;
        MappingGrid.CellFormatting += OnMappingGridCellFormatting;
        MappingGrid.EditingControlShowing += OnMappingGridEditingControlShowing;
        MappingGrid.DataError += OnMappingGridDataError;
        MappingGrid.CellEndEdit += OnMappingGridCellEndEdit;

        foreach (var (oldSid, (name, newSid)) in allMappings)
        {
            var action = newSid != null ? "Migrate" : "Skip";
            var displayName = name;
            if (name == "(unknown)")
                displayName = logic.ResolveDisplayNameForUnknown(oldSid);
            var idx = MappingGrid.Rows.Add(action, displayName, oldSid, newSid ?? "");
            MappingGrid.Rows[idx].Cells["Name"].ReadOnly = true;
            MappingGrid.Rows[idx].Cells["OldSid"].ReadOnly = true;
            MappingGrid.Rows[idx].Cells["NewSid"].ReadOnly = action != "Migrate";
        }

        panel.Controls.Add(MappingGrid);

        var pathsDetail = new ListBox
        {
            Location = new Point(15, 198),
            Size = new Size(560, 100),
            SelectionMode = SelectionMode.None,
            Font = new Font(panel.Font.FontFamily, 8f)
        };
        panel.Controls.Add(pathsDetail);

        MappingGrid.SelectionChanged += (_, _) =>
        {
            pathsDetail.Items.Clear();
            if (MappingGrid.CurrentRow == null)
                return;
            var oldSid = MappingGrid.CurrentRow.Cells["OldSid"].Value?.ToString();
            if (string.IsNullOrEmpty(oldSid))
                return;

            if (orphanedLookup.TryGetValue(oldSid, out var orphan) && orphan.SamplePaths.Count > 0)
            {
                var total = orphan.AceCount + orphan.OwnerCount;
                pathsDetail.Items.Add($"{total} reference(s) found (showing up to {OrphanedSid.MaxSamplePaths} paths):");
                foreach (var p in orphan.SamplePaths)
                    pathsDetail.Items.Add(p);
            }
            else
            {
                pathsDetail.Items.Add("No scan data available.");
            }
        };

        var addButton = new Button
        {
            Text = "Add manually...",
            Location = new Point(15, 306),
            Size = new Size(120, 28),
            FlatStyle = FlatStyle.System
        };
        addButton.Click += (_, _) =>
        {
            var idx = MappingGrid.Rows.Add("Migrate", "(manual)", "", "");
            MappingGrid.Rows[idx].Cells["NewSid"].ReadOnly = false;
        };
        panel.Controls.Add(addButton);

        panel.Controls.Add(new Label
        {
            Text = "Action: Skip = ignore, Migrate = change SID to New SID, Delete = remove from all references.",
            Location = new Point(15, 346),
            Size = new Size(560, 40)
        });
    }

    public List<SidMigrationMapping> CollectMappingsFromGrid()
    {
        var result = new List<SidMigrationMapping>();
        if (MappingGrid == null)
            return result;

        foreach (DataGridViewRow row in MappingGrid.Rows)
        {
            if (row.Cells["Action"].Value?.ToString() != "Migrate")
                continue;
            var oldSid = row.Cells["OldSid"].Value?.ToString()?.Trim() ?? "";
            var newSid = row.Cells["NewSid"].Value?.ToString()?.Trim() ?? "";
            var name = row.Cells["Name"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(oldSid) || string.IsNullOrEmpty(newSid))
                continue;

            // Validate SID format for manually-entered old SIDs
            if (name == "(manual)")
            {
                try
                {
                    _ = new SecurityIdentifier(oldSid);
                }
                catch
                {
                    row.ErrorText = $"'{oldSid}' is not a valid SID.";
                    continue;
                }
            }

            // Validate new SID format (user may type a free-form value in the combo)
            try
            {
                _ = new SecurityIdentifier(newSid);
            }
            catch
            {
                row.ErrorText = $"'{newSid}' is not a valid SID.";
                continue;
            }

            result.Add(new SidMigrationMapping(oldSid, newSid, name));
        }

        return result;
    }

    public List<string> CollectDeleteSidsFromGrid()
    {
        var result = new List<string>();
        if (MappingGrid == null)
            return result;

        result.AddRange(from DataGridViewRow row in MappingGrid.Rows
            where row.Cells["Action"].Value?.ToString() == "Delete"
            select row.Cells["OldSid"].Value?.ToString()?.Trim() ?? ""
            into oldSid
            where !string.IsNullOrEmpty(oldSid)
            select oldSid);

        return result;
    }

    private void OnMappingGridCellValueChanged(object? sender, DataGridViewCellEventArgs args)
    {
        if (args.RowIndex < 0)
            return;
        if (MappingGrid!.Columns[args.ColumnIndex].Name == "Action")
        {
            var action = MappingGrid.Rows[args.RowIndex].Cells["Action"].Value?.ToString();
            MappingGrid.Rows[args.RowIndex].Cells["NewSid"].ReadOnly = action != "Migrate";
        }
    }

    private void OnMappingGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        => AccountGridHelper.PaintSidCell(MappingGrid!, e, "OldSid");

    private void OnMappingGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || MappingGrid!.Columns[e.ColumnIndex].Name != "NewSid")
            return;
        if (e.Value is string sid && _sidDisplayNames.TryGetValue(sid, out var display))
        {
            e.Value = display;
            e.FormattingApplied = true;
        }
    }

    private void OnMappingGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (MappingGrid!.CurrentCell == null ||
            MappingGrid.Columns[MappingGrid.CurrentCell.ColumnIndex].Name != "NewSid")
            return;
        if (e.Control is not ComboBox combo)
            return;

        combo.DropDownStyle = ComboBoxStyle.DropDown;
        combo.Format -= OnNewSidComboFormat;
        combo.Format += OnNewSidComboFormat;
        combo.TextChanged -= OnNewSidComboTextChanged;
        combo.TextChanged += OnNewSidComboTextChanged;
        combo.Validating -= OnNewSidComboValidating;
        combo.Validating += OnNewSidComboValidating;
    }

    private void OnMappingGridDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        if (MappingGrid!.Columns[e.ColumnIndex].Name == "NewSid")
            e.Cancel = true;
    }

    private void OnMappingGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
            MappingGrid!.Rows[e.RowIndex].ErrorText = "";
    }

    private void OnNewSidComboFormat(object? sender, ListControlConvertEventArgs e)
    {
        if (e.Value is string sid && _sidDisplayNames.TryGetValue(sid, out var display))
            e.Value = display;
    }

    private void OnNewSidComboTextChanged(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not string rawSid)
            return;
        var expectedDisplay = _sidDisplayNames.GetValueOrDefault(rawSid, rawSid);
        if (!string.Equals(combo.Text, rawSid, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combo.Text, expectedDisplay, StringComparison.OrdinalIgnoreCase))
            combo.SelectedIndex = -1;
    }

    private void OnNewSidComboValidating(object? sender, CancelEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;

        if (combo.SelectedItem is string selectedRaw && combo.Items.Contains(selectedRaw))
            return;

        var text = combo.Text.Trim() ?? "";
        if (string.IsNullOrEmpty(text) || combo.Items.Contains(text))
            return;

        if (combo.Items.Cast<object?>().Any(item => string.Equals(item?.ToString(), text, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            var sid = new SecurityIdentifier(text);
            var sidStr = sid.Value;
            if (!combo.Items.Contains(sidStr))
            {
                combo.Items.Add(sidStr);
                if (MappingGrid?.Columns["NewSid"] is DataGridViewComboBoxColumn col &&
                    !col.Items.Contains(sidStr))
                    col.Items.Add(sidStr);
            }

            combo.Text = sidStr;
        }
        catch
        {
            e.Cancel = true;
            if (MappingGrid?.CurrentCell != null)
                MappingGrid.Rows[MappingGrid.CurrentCell.RowIndex].ErrorText =
                    $"'{text}' is not a valid SID.";
        }
    }
}
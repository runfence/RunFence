using System.ComponentModel;
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
/// Validation logic is delegated to <see cref="SidMigrationMappingValidator"/>.
/// </summary>
public class SidMigrationMappingBuilder(
    SidMigrationMappingLogic logic,
    SidMigrationMappingValidator validator,
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
            Dock = DockStyle.Fill,
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
                displayName = validator.ResolveSidName(oldSid) ?? "(unknown)";
            var idx = MappingGrid.Rows.Add(action, displayName, oldSid, newSid ?? "");
            MappingGrid.Rows[idx].Cells["Name"].ReadOnly = true;
            MappingGrid.Rows[idx].Cells["OldSid"].ReadOnly = true;
            MappingGrid.Rows[idx].Cells["NewSid"].ReadOnly = action != "Migrate";
        }

        var pathsDetail = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.None,
            Font = new Font(panel.Font.FontFamily, 8f)
        };

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

        var addButton = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 42),
            ToolTipText = "Add manually..."
        };
        addButton.Click += (_, _) =>
        {
            var idx = MappingGrid.Rows.Add("Migrate", "(manual)", "", "");
            MappingGrid.Rows[idx].Cells["NewSid"].ReadOnly = false;
        };
        var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolStrip.Items.Add(addButton);

        var pathsPanel = new Panel { Dock = DockStyle.Bottom, Height = 100 };
        pathsPanel.Controls.Add(pathsDetail);

        panel.Controls.Add(MappingGrid);     // Fill: index 0
        panel.Controls.Add(pathsPanel);      // Bottom: index 1
        panel.Controls.Add(toolStrip);       // Top: index 2 (last added = topmost)
    }

    public bool TryCollectMappingsFromGrid(out List<SidMigrationMapping> mappings)
    {
        var result = new List<SidMigrationMapping>();
        mappings = result;
        if (MappingGrid == null)
            return true;

        var rowsByNewSid = new Dictionary<string, List<DataGridViewRow>>(StringComparer.OrdinalIgnoreCase);
        var validationErrors = new List<string>();

        foreach (DataGridViewRow row in MappingGrid.Rows)
        {
            if (row.Cells["Action"].Value?.ToString() != "Migrate")
                continue;
            var oldSid = row.Cells["OldSid"].Value?.ToString()?.Trim() ?? "";
            var newSidInput = row.Cells["NewSid"].Value?.ToString()?.Trim() ?? "";
            var name = row.Cells["Name"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(oldSid) || string.IsNullOrEmpty(newSidInput))
                continue;

            // Validate SID format for manually-entered old SIDs
            if (name == "(manual)" && !SidMigrationMappingValidator.TryParseSid(oldSid, out _))
            {
                var error = $"'{oldSid}' is not a valid SID.";
                row.ErrorText = error;
                validationErrors.Add(error);
                continue;
            }

            // Validate new SID format (user may type a free-form value in the combo)
            if (!SidMigrationMappingValidator.TryResolveSidInput(newSidInput, _sidDisplayNames, out var newSid))
            {
                var error = $"'{newSidInput}' is not a valid SID.";
                row.ErrorText = error;
                validationErrors.Add(error);
                continue;
            }

            row.Cells["NewSid"].Value = newSid;
            if (!rowsByNewSid.TryGetValue(newSid, out var rowList))
            {
                rowList = [];
                rowsByNewSid[newSid] = rowList;
            }
            rowList.Add(row);

            result.Add(new SidMigrationMapping(oldSid, newSid, name));
        }

        // Check for duplicate NewSid values across rows
        var duplicateNewSids = validator.FindDuplicateNewSids(result);
        if (duplicateNewSids.Count > 0)
        {
            foreach (var (newSid, rows) in rowsByNewSid)
            {
                if (duplicateNewSids.Contains(newSid))
                {
                    const string error = "Duplicate target SID — each old SID must map to a unique new SID.";
                    foreach (var row in rows)
                        row.ErrorText = error;
                    validationErrors.Add($"Duplicate target SID: {newSid}");
                }
            }
        }

        if (validationErrors.Count > 0)
        {
            MessageBox.Show(
                "Please fix the following validation errors before proceeding:\n\n" +
                string.Join("\n", validationErrors),
                "Validation Errors",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            mappings = [];
            return false;
        }

        mappings = result;
        return true;
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
        if (sender is not ComboBox { SelectedItem: string rawSid } combo)
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

        var text = combo.Text.Trim();
        if (string.IsNullOrEmpty(text) || combo.Items.Contains(text))
            return;

        if (combo.Items.Cast<object?>().Any(item => string.Equals(item?.ToString(), text, StringComparison.OrdinalIgnoreCase)))
            return;

        if (SidMigrationMappingValidator.TryResolveSidInput(text, _sidDisplayNames, out var sidStr))
        {
            if (!combo.Items.Contains(sidStr))
            {
                combo.Items.Add(sidStr);
                if (MappingGrid?.Columns["NewSid"] is DataGridViewComboBoxColumn col &&
                    !col.Items.Contains(sidStr))
                    col.Items.Add(sidStr);
            }

            combo.SelectedItem = sidStr;
            combo.Text = sidStr;
        }
        else
        {
            e.Cancel = true;
            if (MappingGrid?.CurrentCell != null)
                MappingGrid.Rows[MappingGrid.CurrentCell.RowIndex].ErrorText =
                    $"'{text}' is not a valid SID.";
        }
    }
}

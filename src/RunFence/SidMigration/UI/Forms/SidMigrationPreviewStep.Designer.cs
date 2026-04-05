#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationPreviewStep
{
    private IContainer components = null;

    private StyledDataGridView _grid;
    private Label _summaryLabel;
    private Label _warningLabel;

    private SidMigrationPreviewStep() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _grid = new StyledDataGridView();
        _summaryLabel = new Label();
        _warningLabel = new Label();

        SuspendLayout();

        // _grid
        _grid.Location = new Point(15, 10);
        _grid.Size = new Size(560, 300);
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("Path", "Path");
        _grid.Columns.Add("Type", "Type");
        _grid.Columns.Add("Changes", "Changes");

        // _summaryLabel
        _summaryLabel.Location = new Point(15, 320);
        _summaryLabel.Size = new Size(560, 25);

        // _warningLabel
        _warningLabel.Location = new Point(15, 345);
        _warningLabel.Size = new Size(560, 25);
        _warningLabel.ForeColor = Color.OrangeRed;
        _warningLabel.Text = "Warning: Large number of changes. This may take a while.";
        _warningLabel.Visible = false;

        Controls.AddRange(new Control[] { _grid, _summaryLabel, _warningLabel });

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 385);
        ResumeLayout(false);
    }
}

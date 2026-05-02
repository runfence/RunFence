#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationPreviewStep
{
    private IContainer components = null;

    private StyledDataGridView _grid;
    private DataGridViewTextBoxColumn Path;
    private DataGridViewTextBoxColumn Type;
    private DataGridViewTextBoxColumn Changes;
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
        Path = new DataGridViewTextBoxColumn();
        Type = new DataGridViewTextBoxColumn();
        Changes = new DataGridViewTextBoxColumn();
        _summaryLabel = new Label();
        _warningLabel = new Label();

        SuspendLayout();
        Size = new Size(595, 385);

        // _grid
        _grid.Location = new Point(15, 10);
        _grid.Size = new Size(560, 300);
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        // Path
        Path.Name = "Path";
        Path.HeaderText = "Path";

        // Type
        Type.Name = "Type";
        Type.HeaderText = "Type";

        // Changes
        Changes.Name = "Changes";
        Changes.HeaderText = "Changes";

        _grid.Columns.AddRange(new DataGridViewColumn[] { Path, Type, Changes });

        // _summaryLabel
        _summaryLabel.Location = new Point(15, 320);
        _summaryLabel.Size = new Size(560, 25);
        _summaryLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // _warningLabel
        _warningLabel.Location = new Point(15, 345);
        _warningLabel.Size = new Size(560, 25);
        _warningLabel.ForeColor = Color.OrangeRed;
        _warningLabel.Text = "Warning: Large number of changes. This may take a while.";
        _warningLabel.Visible = false;
        _warningLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { _grid, _summaryLabel, _warningLabel });

        AutoScaleMode = AutoScaleMode.Font;
        ResumeLayout(false);
    }
}

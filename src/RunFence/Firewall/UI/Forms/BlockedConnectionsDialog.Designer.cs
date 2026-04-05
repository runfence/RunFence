#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Firewall.UI.Forms;

partial class BlockedConnectionsDialog
{
    private IContainer components = null;

    private Panel _auditPanel;
    private CheckBox _auditCheckBox;
    private Label _allUsersNoteLabel;
    private StyledDataGridView _grid;
    private DataGridViewTextBoxColumn _colIp;
    private DataGridViewTextBoxColumn _colHostname;
    private DataGridViewTextBoxColumn _colCount;
    private DataGridViewTextBoxColumn _colLastSeen;
    private DataGridViewTextBoxColumn _colPorts;
    private Panel _bottomPanel;
    private FlowLayoutPanel _rightPanel;
    private RadioButton _ipRadio;
    private RadioButton _domainRadio;
    private Button _addSelectedButton;
    private Button _closeButton;
    private Button _refreshButton;

    private BlockedConnectionsDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _auditPanel = new Panel();
        _auditCheckBox = new CheckBox();
        _allUsersNoteLabel = new Label();
        _grid = new StyledDataGridView();
        _colIp = new DataGridViewTextBoxColumn();
        _colHostname = new DataGridViewTextBoxColumn();
        _colCount = new DataGridViewTextBoxColumn();
        _colLastSeen = new DataGridViewTextBoxColumn();
        _colPorts = new DataGridViewTextBoxColumn();
        _bottomPanel = new Panel();
        _rightPanel = new FlowLayoutPanel();
        _ipRadio = new RadioButton();
        _domainRadio = new RadioButton();
        _addSelectedButton = new Button();
        _closeButton = new Button();
        _refreshButton = new Button();

        _auditPanel.SuspendLayout();
        _rightPanel.SuspendLayout();
        _bottomPanel.SuspendLayout();
        ((ISupportInitialize)_grid).BeginInit();
        SuspendLayout();

        // _auditCheckBox
        _auditCheckBox.Text = "Enable connection audit logging (required for events to appear)";
        _auditCheckBox.AutoSize = true;
        _auditCheckBox.Location = new Point(8, 6);

        // _allUsersNoteLabel
        _allUsersNoteLabel.Text = "Note: shows blocked connections for all users, not just this account.";
        _allUsersNoteLabel.AutoSize = true;
        _allUsersNoteLabel.ForeColor = SystemColors.GrayText;
        _allUsersNoteLabel.Location = new Point(8, 32);

        // _auditPanel
        _auditPanel.Dock = DockStyle.Top;
        _auditPanel.Height = 52;
        _auditPanel.Controls.Add(_auditCheckBox);
        _auditPanel.Controls.Add(_allUsersNoteLabel);

        // columns
        _colIp.Name = "colIp";
        _colIp.HeaderText = "IP Address";
        _colIp.ReadOnly = true;
        _colIp.FillWeight = 22;

        _colHostname.Name = "colHostname";
        _colHostname.HeaderText = "Hostname";
        _colHostname.ReadOnly = true;
        _colHostname.FillWeight = 28;

        _colCount.Name = "colCount";
        _colCount.HeaderText = "Hits";
        _colCount.ReadOnly = true;
        _colCount.FillWeight = 8;

        _colLastSeen.Name = "colLastSeen";
        _colLastSeen.HeaderText = "Last Seen";
        _colLastSeen.ReadOnly = true;
        _colLastSeen.FillWeight = 17;

        _colPorts.Name = "colPorts";
        _colPorts.HeaderText = "Ports";
        _colPorts.ReadOnly = true;
        _colPorts.FillWeight = 20;

        // _grid
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.AddRange(new DataGridViewColumn[]
        {
            _colIp, _colHostname, _colCount, _colLastSeen, _colPorts
        });

        // _ipRadio
        _ipRadio.Text = "Add as IP";
        _ipRadio.Checked = true;
        _ipRadio.AutoSize = true;
        _ipRadio.Margin = new Padding(4, 8, 4, 0);

        // _domainRadio
        _domainRadio.Text = "Add as Domain";
        _domainRadio.AutoSize = true;
        _domainRadio.Margin = new Padding(4, 8, 12, 0);

        // _addSelectedButton
        _addSelectedButton.Text = "Add Selected";
        _addSelectedButton.Size = new Size(100, 28);
        _addSelectedButton.FlatStyle = FlatStyle.System;
        _addSelectedButton.Click += OnAddSelectedClick;
        _addSelectedButton.Margin = new Padding(4, 6, 4, 0);

        // _closeButton
        _closeButton.Text = "Close";
        _closeButton.Size = new Size(80, 28);
        _closeButton.FlatStyle = FlatStyle.System;
        _closeButton.Click += OnCloseClick;
        _closeButton.Margin = new Padding(4, 6, 8, 0);

        // _rightPanel — right-to-left: Close | AddSelected | Domain | IP
        _rightPanel.Dock = DockStyle.Right;
        _rightPanel.AutoSize = true;
        _rightPanel.FlowDirection = FlowDirection.RightToLeft;
        _rightPanel.WrapContents = false;
        _rightPanel.Controls.Add(_closeButton);
        _rightPanel.Controls.Add(_addSelectedButton);
        _rightPanel.Controls.Add(_domainRadio);
        _rightPanel.Controls.Add(_ipRadio);

        // _refreshButton — anchored left in _bottomPanel
        _refreshButton.Text = "Refresh";
        _refreshButton.Size = new Size(80, 28);
        _refreshButton.FlatStyle = FlatStyle.System;
        _refreshButton.Click += OnRefreshClick;
        _refreshButton.Location = new Point(8, 8);

        // _bottomPanel
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Height = 44;
        _bottomPanel.Controls.Add(_rightPanel);
        _bottomPanel.Controls.Add(_refreshButton);

        // BlockedConnectionsDialog
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(800, 500);
        MinimumSize = new Size(600, 350);
        Controls.Add(_grid);
        Controls.Add(_auditPanel);
        Controls.Add(_bottomPanel);

        ((ISupportInitialize)_grid).EndInit();
        _auditPanel.ResumeLayout(false);
        _auditPanel.PerformLayout();
        _rightPanel.ResumeLayout(false);
        _rightPanel.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

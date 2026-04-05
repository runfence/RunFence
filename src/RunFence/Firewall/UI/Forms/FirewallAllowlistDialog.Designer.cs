#nullable disable

using System.ComponentModel;
using RunFence.UI;
using RunFence.UI.Controls;

namespace RunFence.Firewall.UI.Forms;

partial class FirewallAllowlistDialog
{
    private IContainer components = null;

    private Label _warningLabel;
    private StyledDataGridView _grid;
    private DataGridViewTextBoxColumn _typeCol;
    private DataGridViewTextBoxColumn _valueCol;
    private DataGridViewTextBoxColumn _resolvedCol;
    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _removeButton;
    private ToolStripButton _exportButton;
    private ToolStripButton _importButton;
    private ToolStripSeparator _exportImportSeparator;
    private ToolStripButton _resolveButton;
    private ToolStripSeparator _viewBlockedSeparator;
    private ToolStripButton _viewBlockedButton;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxRemoveItem;
    private ToolStripMenuItem _ctxExportItem;
    private Label _dnsLabel;
    private FlowLayoutPanel _firewallSettingsPanel;
    private Label _firewallSettingsLabel;
    private CheckBox _allowInternetCheckBox;
    private CheckBox _allowLanCheckBox;
    private CheckBox _allowLocalhostCheckBox;
    private FlowLayoutPanel _buttonPanel;
    private Button _okButton;
    private Button _cancelButton;

    private FirewallAllowlistDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _warningLabel = new Label();
        _grid = new StyledDataGridView();
        _typeCol = new DataGridViewTextBoxColumn();
        _valueCol = new DataGridViewTextBoxColumn();
        _resolvedCol = new DataGridViewTextBoxColumn();
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _exportButton = new ToolStripButton();
        _importButton = new ToolStripButton();
        _exportImportSeparator = new ToolStripSeparator();
        _resolveButton = new ToolStripButton();
        _viewBlockedSeparator = new ToolStripSeparator();
        _viewBlockedButton = new ToolStripButton();
        _contextMenu = new ContextMenuStrip(components);
        _ctxAdd = new ToolStripMenuItem();
        _ctxRemoveItem = new ToolStripMenuItem();
        _ctxExportItem = new ToolStripMenuItem();
        _dnsLabel = new Label();
        _firewallSettingsPanel = new FlowLayoutPanel();
        _firewallSettingsLabel = new Label();
        _allowInternetCheckBox = new CheckBox();
        _allowLanCheckBox = new CheckBox();
        _allowLocalhostCheckBox = new CheckBox();
        _buttonPanel = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();

        ((ISupportInitialize)_grid).BeginInit();
        _toolStrip.SuspendLayout();
        _firewallSettingsPanel.SuspendLayout();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _warningLabel
        _warningLabel.Text = "Whitelist entries only apply when Internet or LAN access is blocked";
        _warningLabel.Dock = DockStyle.Top;
        _warningLabel.Height = 30;
        _warningLabel.TextAlign = ContentAlignment.MiddleLeft;
        _warningLabel.Padding = new Padding(8, 0, 8, 0);
        _warningLabel.BackColor = Color.FromArgb(0xFF, 0xF0, 0x80);
        _warningLabel.ForeColor = Color.FromArgb(0x60, 0x40, 0x00);
        _warningLabel.Visible = false;

        // _typeCol
        _typeCol.Name = "Type";
        _typeCol.HeaderText = "Type";
        _typeCol.FillWeight = 15;
        _typeCol.ReadOnly = true;

        // _valueCol
        _valueCol.Name = "Value";
        _valueCol.HeaderText = "Address / Domain";
        _valueCol.FillWeight = 55;
        _valueCol.ReadOnly = false;

        // _resolvedCol
        _resolvedCol.Name = "Resolved";
        _resolvedCol.HeaderText = "Resolved IPs";
        _resolvedCol.FillWeight = 30;
        _resolvedCol.ReadOnly = true;

        // _grid
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.AddRange(new DataGridViewColumn[] { _typeCol, _valueCol, _resolvedCol });
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.CellEndEdit += OnGridCellEndEdit;
        _grid.KeyDown += OnGridKeyDown;
        _grid.MouseDown += OnGridMouseDown;
        _grid.ContextMenuStrip = _contextMenu;

        // _addButton
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 30);
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add entry (IP, CIDR, or domain — auto-detected)";
        _addButton.Click += OnAddClick;

        // _removeButton
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 30);
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _exportButton
        _exportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 30);
        _exportButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _exportButton.ToolTipText = "Export selected entries to file (exports all if none selected)";
        _exportButton.Click += OnExportClick;

        // _importButton
        _importButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0xCC), 30);
        _importButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _importButton.ToolTipText = "Import entries from file";
        _importButton.Click += OnImportClick;

        // _resolveButton
        _resolveButton.Image = UiIconFactory.CreateToolbarIcon("\u21BB", Color.FromArgb(0x33, 0x66, 0x99), 30);
        _resolveButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _resolveButton.ToolTipText = "Resolve DNS";
        _resolveButton.Click += OnResolveClick;

        // _viewBlockedButton
        _viewBlockedButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F6AB", Color.FromArgb(0xCC, 0x44, 0x00), 30);
        _viewBlockedButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        _viewBlockedButton.Text = "View Blocked";
        _viewBlockedButton.ToolTipText = "View recently blocked outbound connections for this account";
        _viewBlockedButton.Enabled = false;
        _viewBlockedButton.Click += OnViewBlockedClick;

        // _ctxAdd
        _ctxAdd.Text = "Add...";
        _ctxAdd.Click += OnAddClick;

        // _ctxRemoveItem
        _ctxRemoveItem.Text = "Remove";
        _ctxRemoveItem.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _ctxRemoveItem.Click += OnRemoveClick;

        // _ctxExportItem
        _ctxExportItem.Text = "Export Selected";
        _ctxExportItem.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxExportItem.Click += OnExportClick;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxRemoveItem, _ctxExportItem });
        _contextMenu.Opening += OnContextMenuOpening;

        // _toolStrip
        _toolStrip.ImageScalingSize = new Size(30, 30);
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _removeButton, _exportButton, _importButton, _exportImportSeparator, _resolveButton, _viewBlockedSeparator, _viewBlockedButton });

        // _dnsLabel
        _dnsLabel.Dock = DockStyle.Bottom;
        _dnsLabel.Height = 22;
        _dnsLabel.TextAlign = ContentAlignment.MiddleLeft;
        _dnsLabel.Padding = new Padding(8, 0, 8, 0);
        _dnsLabel.Font = new Font(SystemFonts.DefaultFont, FontStyle.Italic);

        // _firewallSettingsLabel
        _firewallSettingsLabel.Text = "Allow:";
        _firewallSettingsLabel.AutoSize = true;
        _firewallSettingsLabel.Margin = new Padding(0, 5, 6, 0);

        // _allowInternetCheckBox
        _allowInternetCheckBox.Text = "Internet";
        _allowInternetCheckBox.AutoSize = true;
        _allowInternetCheckBox.Checked = true;
        _allowInternetCheckBox.Margin = new Padding(0, 3, 12, 0);
        _allowInternetCheckBox.CheckedChanged += OnFirewallSettingsChanged;

        // _allowLanCheckBox
        _allowLanCheckBox.Text = "LAN";
        _allowLanCheckBox.AutoSize = true;
        _allowLanCheckBox.Checked = true;
        _allowLanCheckBox.Margin = new Padding(0, 3, 12, 0);
        _allowLanCheckBox.CheckedChanged += OnFirewallSettingsChanged;

        // _allowLocalhostCheckBox
        _allowLocalhostCheckBox.Text = "Localhost";
        _allowLocalhostCheckBox.AutoSize = true;
        _allowLocalhostCheckBox.Checked = true;
        _allowLocalhostCheckBox.Margin = new Padding(0, 3, 0, 0);
        _allowLocalhostCheckBox.CheckedChanged += OnFirewallSettingsChanged;

        // _firewallSettingsPanel
        _firewallSettingsPanel.Dock = DockStyle.Bottom;
        _firewallSettingsPanel.Height = 30;
        _firewallSettingsPanel.FlowDirection = FlowDirection.LeftToRight;
        _firewallSettingsPanel.WrapContents = false;
        _firewallSettingsPanel.Padding = new Padding(8, 2, 8, 0);
        _firewallSettingsPanel.Controls.Add(_firewallSettingsLabel);
        _firewallSettingsPanel.Controls.Add(_allowInternetCheckBox);
        _firewallSettingsPanel.Controls.Add(_allowLanCheckBox);
        _firewallSettingsPanel.Controls.Add(_allowLocalhostCheckBox);

        // _okButton
        _okButton.Text = "OK";
        _okButton.Size = new Size(80, 28);
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(80, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Click += OnCancelClick;

        // _buttonPanel — FlowLayoutPanel so buttons are always visible regardless of DPI/size
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 44;
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.WrapContents = false;
        _buttonPanel.Padding = new Padding(8, 7, 8, 0);
        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Controls.Add(_okButton);

        // FirewallAllowlistDialog
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 450);
        MinimumSize = new Size(530, 330);
        AcceptButton = _okButton;
        Controls.Add(_grid);
        Controls.Add(_warningLabel);
        Controls.Add(_toolStrip);
        Controls.Add(_dnsLabel);
        Controls.Add(_firewallSettingsPanel);
        Controls.Add(_buttonPanel);

        ((ISupportInitialize)_grid).EndInit();
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _firewallSettingsPanel.ResumeLayout(false);
        _firewallSettingsPanel.PerformLayout();
        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

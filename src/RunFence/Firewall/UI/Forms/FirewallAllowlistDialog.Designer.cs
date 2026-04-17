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
    private CheckBox _filterEphemeralCheckBox;
    private Panel _buttonPanel;
    private Panel _buttonSpacer;
    private Button _applyButton;
    private Button _closeButton;
    private TabControl _tabControl;
    private TabPage _allowlistTab;
    private TabPage _portsTab;
    private Panel _allowlistPanel;
    private Panel _portsPanel;
    private Label _portsWarningLabel;
    private StyledDataGridView _portsGrid;
    private DataGridViewTextBoxColumn _portCol;
    private ContextMenuStrip _portsContextMenu;
    private ToolStripMenuItem _portsCtxAdd;
    private ToolStripMenuItem _portsCtxRemove;
    private ToolStripMenuItem _portsCtxExport;

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
        _filterEphemeralCheckBox = new CheckBox();
        _buttonPanel = new Panel();
        _buttonSpacer = new Panel();
        _applyButton = new Button();
        _closeButton = new Button();
        _tabControl = new TabControl();
        _allowlistTab = new TabPage();
        _portsTab = new TabPage();
        _allowlistPanel = new Panel();
        _portsPanel = new Panel();
        _portsWarningLabel = new Label();
        _portsGrid = new StyledDataGridView();
        _portCol = new DataGridViewTextBoxColumn();
        _portsContextMenu = new ContextMenuStrip(components);
        _portsCtxAdd = new ToolStripMenuItem();
        _portsCtxRemove = new ToolStripMenuItem();
        _portsCtxExport = new ToolStripMenuItem();

        ((ISupportInitialize)_grid).BeginInit();
        _toolStrip.SuspendLayout();
        _firewallSettingsPanel.SuspendLayout();
        _buttonPanel.SuspendLayout();
        _allowlistPanel.SuspendLayout();
        ((ISupportInitialize)_portsGrid).BeginInit();
        _portsPanel.SuspendLayout();
        _allowlistTab.SuspendLayout();
        _portsTab.SuspendLayout();
        _tabControl.SuspendLayout();
        SuspendLayout();

        // _warningLabel
        _warningLabel.Text = "Allowlist entries only apply when Internet or LAN access is blocked";
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
        _addButton.Click += OnAddButtonClick;

        // _removeButton
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 30);
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveButtonClick;

        // _exportButton
        _exportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 30);
        _exportButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _exportButton.ToolTipText = "Export selected entries to file (exports all entries and ports when nothing is selected)";
        _exportButton.Click += OnExportButtonClick;

        // _importButton
        _importButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0xCC), 30);
        _importButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _importButton.ToolTipText = "Import entries and ports from file";
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
        _ctxRemoveItem.Visible = false;
        _ctxRemoveItem.Click += OnRemoveClick;

        // _ctxExportItem
        _ctxExportItem.Text = "Export Selected";
        _ctxExportItem.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxExportItem.Visible = false;
        _ctxExportItem.Click += OnExportClick;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxRemoveItem, _ctxExportItem });
        _contextMenu.Opening += OnContextMenuOpening;

        // _toolStrip — lives at form level, above the tab control
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

        // _allowlistPanel
        _allowlistPanel.Dock = DockStyle.Fill;
        _allowlistPanel.Controls.Add(_grid);
        _allowlistPanel.Controls.Add(_warningLabel);
        _allowlistPanel.Controls.Add(_dnsLabel);

        // _allowlistTab
        _allowlistTab.Text = "Internet";
        _allowlistTab.Controls.Add(_allowlistPanel);

        // _portsWarningLabel
        _portsWarningLabel.Text = "Port exceptions only apply when Localhost access is blocked";
        _portsWarningLabel.Dock = DockStyle.Top;
        _portsWarningLabel.Height = 30;
        _portsWarningLabel.TextAlign = ContentAlignment.MiddleLeft;
        _portsWarningLabel.Padding = new Padding(8, 0, 8, 0);
        _portsWarningLabel.BackColor = Color.FromArgb(0xFF, 0xF0, 0x80);
        _portsWarningLabel.ForeColor = Color.FromArgb(0x60, 0x40, 0x00);
        _portsWarningLabel.Visible = false;

        // _portCol
        _portCol.Name = "Port";
        _portCol.HeaderText = "Port";
        _portCol.FillWeight = 100;
        _portCol.ReadOnly = false;

        // _portsGrid
        _portsGrid.AllowUserToAddRows = false;
        _portsGrid.AllowUserToDeleteRows = false;
        _portsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _portsGrid.MultiSelect = true;
        _portsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _portsGrid.Dock = DockStyle.Fill;
        _portsGrid.Columns.AddRange(new DataGridViewColumn[] { _portCol });
        _portsGrid.SelectionChanged += OnPortsGridSelectionChanged;
        _portsGrid.KeyDown += OnPortsGridKeyDown;
        _portsGrid.CellEndEdit += OnPortsGridCellEndEdit;
        _portsGrid.MouseDown += OnPortsGridMouseDown;
        _portsGrid.ContextMenuStrip = _portsContextMenu;

        // _portsCtxAdd
        _portsCtxAdd.Text = "Add...";
        _portsCtxAdd.Click += OnPortsAddClick;

        // _portsCtxRemove
        _portsCtxRemove.Text = "Remove";
        _portsCtxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _portsCtxRemove.Visible = false;
        _portsCtxRemove.Click += OnPortsRemoveClick;

        // _portsCtxExport
        _portsCtxExport.Text = "Export Selected";
        _portsCtxExport.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _portsCtxExport.Visible = false;
        _portsCtxExport.Click += OnPortsExportClick;

        // _portsContextMenu
        _portsContextMenu.Items.AddRange(new ToolStripItem[] { _portsCtxAdd, _portsCtxRemove, _portsCtxExport });
        _portsContextMenu.Opening += OnPortsContextMenuOpening;

        // _portsPanel
        _portsPanel.Dock = DockStyle.Fill;
        _portsPanel.Controls.Add(_portsGrid);
        _portsPanel.Controls.Add(_portsWarningLabel);

        // _portsTab
        _portsTab.Text = "Localhost";
        _portsTab.Controls.Add(_portsPanel);

        // _tabControl
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.TabPages.Add(_allowlistTab);
        _tabControl.TabPages.Add(_portsTab);
        _tabControl.SelectedIndexChanged += OnTabChanged;

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

        // _filterEphemeralCheckBox
        _filterEphemeralCheckBox.Text = "Filter loopback 49152-65535";
        _filterEphemeralCheckBox.AutoSize = true;
        _filterEphemeralCheckBox.Margin = new Padding(16, 3, 0, 0);
        _filterEphemeralCheckBox.CheckedChanged += OnFirewallSettingsChanged;

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
        _firewallSettingsPanel.Controls.Add(_filterEphemeralCheckBox);

        // _applyButton
        _applyButton.Text = "Apply";
        _applyButton.Width = 80;
        _applyButton.Dock = DockStyle.Right;
        _applyButton.FlatStyle = FlatStyle.System;
        _applyButton.Enabled = false;
        _applyButton.Click += OnApplyClick;

        // _closeButton
        _closeButton.Text = "Close";
        _closeButton.Width = 80;
        _closeButton.Dock = DockStyle.Right;
        _closeButton.FlatStyle = FlatStyle.System;
        _closeButton.Click += OnCloseClick;

        // _buttonSpacer
        _buttonSpacer.Width = 6;
        _buttonSpacer.Dock = DockStyle.Right;

        // _buttonPanel — plain Panel with right-docked buttons for reliable right-edge alignment
        // Padding (8,8,8,8) on 44px height gives 28px inner height = button height
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 44;
        _buttonPanel.Padding = new Padding(8, 8, 8, 8);
        _buttonPanel.Controls.Add(_applyButton);
        _buttonPanel.Controls.Add(_buttonSpacer);
        _buttonPanel.Controls.Add(_closeButton);

        // FirewallAllowlistDialog
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 450);
        MinimumSize = new Size(530, 330);
        Controls.Add(_tabControl);
        Controls.Add(_firewallSettingsPanel);
        Controls.Add(_buttonPanel);
        Controls.Add(_toolStrip);

        ((ISupportInitialize)_grid).EndInit();
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _firewallSettingsPanel.ResumeLayout(false);
        _firewallSettingsPanel.PerformLayout();
        _buttonPanel.ResumeLayout(false);
        _allowlistPanel.ResumeLayout(false);
        _allowlistPanel.PerformLayout();
        ((ISupportInitialize)_portsGrid).EndInit();
        _portsPanel.ResumeLayout(false);
        _portsPanel.PerformLayout();
        _allowlistTab.ResumeLayout(false);
        _portsTab.ResumeLayout(false);
        _tabControl.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

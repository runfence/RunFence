#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Account.UI.Forms;

partial class AccountsPanel
{
    private IContainer components = null;

    private StyledDataGridView _grid;
    private Label _statusLabel;
    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _importButton;
    private ToolStripButton _openCmdButton;
    private ToolStripButton _openFolderBrowserButton;
    private ToolStripButton _accountsButton;
    private ToolStripButton _copyPasswordButton;
    private ToolStripButton _refreshButton;
    private ToolStripButton _migrateSidsButton;
    private ToolStripButton _deleteProfilesButton;
    private ToolStripButton _createUserButton;
    private ToolStripButton _createContainerButton;
    private ToolStripButton _aclManagerButton;
    private ToolStripButton _scanAclsButton;
    private ToolStripButton _firewallButton;
    private ToolStripButton _wizardButton;
    private ToolStripSeparator _toolStripSep1;
    private ToolStripSeparator _toolStripSep2;
    private ContextMenuStrip _contextMenu;
    private ContextMenuStrip _headerContextMenu;
    private DataGridViewCheckBoxColumn _allowInternetCol;
    private ToolStripMenuItem _hdrAdd;
    private ToolStripMenuItem _hdrCreateUser;
    private ToolStripMenuItem _hdrCreateContainer;
    private Panel _statusPanel;
    private DataGridViewCheckBoxColumn _importCol;
    private DataGridViewTextBoxColumn _accountCol;
    private DataGridViewImageColumn _credentialCol;
    private DataGridViewCheckBoxColumn _logonCol;
    private DataGridViewTextBoxColumn _appsCol;
    private DataGridViewTextBoxColumn _profilePathCol;
    private DataGridViewTextBoxColumn _sidCol;

    private AccountsPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_parentForm != null)
            {
                _parentForm.Resize -= OnParentFormResize;
                _parentForm = null;
            }
            _timerCoordinator?.Stop();
            _processDisplayManager?.Dispose();
            _credentialHandler?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _grid = new StyledDataGridView();
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _importButton = new ToolStripButton();
        _openCmdButton = new ToolStripButton();
        _openFolderBrowserButton = new ToolStripButton();
        _accountsButton = new ToolStripButton();
        _copyPasswordButton = new ToolStripButton();
        _refreshButton = new ToolStripButton();
        _migrateSidsButton = new ToolStripButton();
        _deleteProfilesButton = new ToolStripButton();
        _createUserButton = new ToolStripButton();
        _createContainerButton = new ToolStripButton();
        _aclManagerButton = new ToolStripButton();
        _scanAclsButton = new ToolStripButton();
        _firewallButton = new ToolStripButton();
        _wizardButton = new ToolStripButton();
        _toolStripSep1 = new ToolStripSeparator();
        _toolStripSep2 = new ToolStripSeparator();
        _contextMenu = new ContextMenuStrip();
        _headerContextMenu = new ContextMenuStrip();
        _hdrAdd = new ToolStripMenuItem();
        _hdrCreateUser = new ToolStripMenuItem();
        _hdrCreateContainer = new ToolStripMenuItem();
        _statusLabel = new Label();

        ((ISupportInitialize)_grid).BeginInit();
        _toolStrip.SuspendLayout();
        _contextMenu.SuspendLayout();
        _headerContextMenu.SuspendLayout();
        SuspendLayout();

        // _grid columns
        _importCol = new DataGridViewCheckBoxColumn();
        _importCol.Name = "Import";
        _importCol.HeaderText = "For Import";
        _importCol.FillWeight = 12;
        _importCol.Visible = false;
        _importCol.SortMode = DataGridViewColumnSortMode.NotSortable;

        _accountCol = new DataGridViewTextBoxColumn();
        _accountCol.Name = "Account";
        _accountCol.HeaderText = "Account";
        _accountCol.FillWeight = 22;

        _credentialCol = new DataGridViewImageColumn();
        _credentialCol.Name = "Credential";
        _credentialCol.HeaderText = "";
        _credentialCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _credentialCol.Width = 28;
        _credentialCol.ReadOnly = true;
        _credentialCol.ImageLayout = DataGridViewImageCellLayout.Zoom;
        _credentialCol.SortMode = DataGridViewColumnSortMode.NotSortable;
        _credentialCol.ToolTipText = "Credential";
        _credentialCol.Visible = false;

        _logonCol = new DataGridViewCheckBoxColumn();
        _logonCol.Name = "Logon";
        _logonCol.HeaderText = "Logon";
        _logonCol.FillWeight = 10;
        _logonCol.SortMode = DataGridViewColumnSortMode.NotSortable;
        _logonCol.ToolTipText = "Allow interactive logon. Unchecked = hide from logon screen and run a logoff script.\nIndeterminate = only one of the two conditions is set.";

        _allowInternetCol = new DataGridViewCheckBoxColumn();
        _allowInternetCol.Name = "colAllowInternet";
        _allowInternetCol.HeaderText = "Internet";
        _allowInternetCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _allowInternetCol.Width = 65;
        _allowInternetCol.ReadOnly = false;
        _allowInternetCol.SortMode = DataGridViewColumnSortMode.NotSortable;
        _allowInternetCol.ToolTipText = "Allow outbound internet traffic for this account.\nUncheck to block all internet-bound outbound traffic.";

        _appsCol = new DataGridViewTextBoxColumn();
        _appsCol.Name = "Apps";
        _appsCol.HeaderText = "Apps";
        _appsCol.FillWeight = 18;
        _appsCol.ReadOnly = true;

        _profilePathCol = new DataGridViewTextBoxColumn();
        _profilePathCol.Name = "ProfilePath";
        _profilePathCol.HeaderText = "Profile Path";
        _profilePathCol.FillWeight = 18;
        _profilePathCol.ReadOnly = true;
        _profilePathCol.ToolTipText = "Profile folder path";

        _sidCol = new DataGridViewTextBoxColumn();
        _sidCol.Name = "SID";
        _sidCol.HeaderText = "SID";
        _sidCol.FillWeight = 14;
        _sidCol.MinimumWidth = 60;
        _sidCol.ReadOnly = true;

        _grid.Columns.AddRange(new DataGridViewColumn[]
            { _importCol, _credentialCol, _accountCol, _logonCol, _allowInternetCol, _appsCol, _profilePathCol, _sidCol });

        // _grid — shared visual styling from DataPanel, then unlock for editable checkboxes
        ConfigureReadOnlyGrid(_grid);
        _grid.ReadOnly = false;
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        _grid.CellMouseClick += OnGridCellMouseClick;
        _grid.CellContentClick += OnGridCellContentClick;
        _grid.KeyDown += OnGridKeyDown;

        // _toolStrip
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(36, 36);
        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _refreshButton, _wizardButton, _createUserButton, _createContainerButton, _addButton,
            _toolStripSep1, _openCmdButton, _openFolderBrowserButton, _aclManagerButton, _firewallButton, _toolStripSep2, _scanAclsButton,
            _copyPasswordButton, _accountsButton, _deleteProfilesButton, _migrateSidsButton, _importButton
        });

        // _refreshButton
        _refreshButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _refreshButton.ToolTipText = "Refresh";
        _refreshButton.Click += OnRefreshClick;

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add credential";
        _addButton.Click += OnAddClick;

        // _createUserButton
        _createUserButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _createUserButton.ToolTipText = "Create local account";
        _createUserButton.Click += OnCreateUserClick;

        // _createContainerButton
        _createContainerButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _createContainerButton.ToolTipText = "Create App Container";
        _createContainerButton.Click += OnCreateContainerClick;

        // _openCmdButton
        _openCmdButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _openCmdButton.ToolTipText = "CMD";
        _openCmdButton.Enabled = false;
        _openCmdButton.Click += OnOpenCmdClick;

        // _openFolderBrowserButton
        _openFolderBrowserButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _openFolderBrowserButton.ToolTipText = "Folder Browser";
        _openFolderBrowserButton.Enabled = false;
        _openFolderBrowserButton.Click += OnOpenFolderBrowserClick;

        // _aclManagerButton
        _aclManagerButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _aclManagerButton.ToolTipText = "ACL Manager";
        _aclManagerButton.Enabled = false;

        // _scanAclsButton
        _scanAclsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _scanAclsButton.ToolTipText = "Scan ACLs";
        _scanAclsButton.Click += OnScanAclsClick;

        // _firewallButton
        _firewallButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _firewallButton.Enabled = false;

        // _wizardButton
        _wizardButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _wizardButton.ToolTipText = "Setup Wizard";

        // _copyPasswordButton
        _copyPasswordButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _copyPasswordButton.ToolTipText = "Copy random password";
        _copyPasswordButton.Alignment = ToolStripItemAlignment.Right;
        _copyPasswordButton.Click += OnCopyRandomPasswordClick;

        // _importButton
        _importButton.Text = "Import Desktop Settings...";
        _importButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _importButton.ToolTipText = "Import Desktop Settings";
        _importButton.Alignment = ToolStripItemAlignment.Right;
        _importButton.Click += OnImportClick;

        // _accountsButton
        _accountsButton.Text = "Accounts Control...";
        _accountsButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _accountsButton.ToolTipText = "Accounts Control";
        _accountsButton.Alignment = ToolStripItemAlignment.Right;
        _accountsButton.Click += OnOpenAccountsClick;

        // _migrateSidsButton
        _migrateSidsButton.Text = "Migrate SIDs...";
        _migrateSidsButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _migrateSidsButton.ToolTipText = "Migrate SIDs...";
        _migrateSidsButton.Alignment = ToolStripItemAlignment.Right;
        _migrateSidsButton.Click += OnMigrateSidsClick;

        // _deleteProfilesButton
        _deleteProfilesButton.Text = "Orphaned Profiles...";
        _deleteProfilesButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _deleteProfilesButton.ToolTipText = "Delete orphaned profile directories";
        _deleteProfilesButton.Alignment = ToolStripItemAlignment.Right;
        _deleteProfilesButton.Click += OnDeleteProfilesClick;

        // _contextMenu — context menu items are built by AccountContextMenuOrchestrator.Initialize
        _contextMenu.Opening += OnContextMenuOpening;

        // _hdrAdd / _hdrCreateUser
        _hdrAdd.Text = "Add credential";
        _hdrAdd.Click += OnAddClick;

        _hdrCreateUser.Text = "Create local account";
        _hdrCreateUser.Click += OnCreateUserClick;

        _hdrCreateContainer.Text = "Create App Container...";

        // _headerContextMenu
        _headerContextMenu.Items.AddRange(new ToolStripItem[] { _hdrAdd, _hdrCreateUser, _hdrCreateContainer });

        // _statusLabel
        _statusLabel.Text = "Ready";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _statusPanel = new Panel();
        _statusPanel.Dock = DockStyle.Bottom;
        _statusPanel.Height = 25;
        _statusPanel.Padding = new Padding(5, 2, 5, 2);
        _statusPanel.Controls.Add(_statusLabel);

        // AccountsPanel
        Dock = DockStyle.Fill;
        Controls.Add(_grid);
        Controls.Add(_statusPanel);
        Controls.Add(_toolStrip);

        ((ISupportInitialize)_grid).EndInit();
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _contextMenu.ResumeLayout(false);
        _headerContextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

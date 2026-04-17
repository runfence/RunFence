#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Acl.UI.Forms;

partial class AclManagerDialog
{
    private IContainer components = null;

    private TabControl _tabControl;
    private TabPage _grantsTab;
    private TabPage _traverseTab;
    private StyledDataGridView _grantsGrid;
    private StyledDataGridView _traverseGrid;
    private ToolStrip _toolStrip;
    private ToolStripButton _addFileButton;
    private ToolStripButton _addFolderButton;
    private ToolStripButton _scanButton;
    private ToolStripLabel _scanStatusLabel;
    private ToolStripButton _removeButton;
    private ToolStripButton _fixAclsButton;
    private Button _applyButton;
    private ToolStripButton _exportButton;
    private ToolStripButton _importButton;
    private ToolStripProgressBar _progressBar;
    private Button _closeButton;
    private ContextMenuStrip _contextMenuGrants;
    private ToolStripMenuItem _ctxAddFile;
    private ToolStripMenuItem _ctxAddFolder;
    private ToolStripMenuItem _ctxRemove;
    private ToolStripMenuItem _ctxFixAcls;
    private ToolStripSeparator _ctxGrantsSep;
    private ContextMenuStrip _contextMenuTraverse;
    private ToolStripMenuItem _ctxTraverseAddFile;
    private ToolStripMenuItem _ctxTraverseAddFolder;
    private ToolStripMenuItem _ctxTraverseRemove;
    private ToolStripMenuItem _ctxTraverseFixAcls;
    private ToolStripSeparator _ctxTraverseSep;
    private ToolStripMenuItem _ctxUntrack;
    private ToolStripSeparator _ctxGrantsOpenFolderSep;
    private ToolStripMenuItem _ctxOpenFolderGrants;
    private ToolStripMenuItem _ctxCopyPathGrants;
    private ToolStripSeparator _ctxGrantsPropertiesSep;
    private ToolStripMenuItem _ctxPropertiesGrants;
    private ToolStripMenuItem _ctxTraverseUntrack;
    private ToolStripSeparator _ctxTraverseOpenFolderSep;
    private ToolStripMenuItem _ctxTraverseOpenFolder;
    private ToolStripMenuItem _ctxTraverseCopyPath;
    private ToolStripSeparator _ctxTraversePropertiesSep;
    private ToolStripMenuItem _ctxTraverseProperties;
    private Icon _formIcon;

    private AclManagerDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _traverseHelper?.DisposeBoldFont();
            _formIcon?.Dispose();
            _modificationHandler?.CancelScanCts?.Dispose();
            _grantsDropInterceptor?.Dispose();
            _traverseDropInterceptor?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();

        _tabControl = new TabControl();
        _grantsTab = new TabPage();
        _traverseTab = new TabPage();
        _grantsGrid = new StyledDataGridView();
        _traverseGrid = new StyledDataGridView();
        _toolStrip = new ToolStrip();
        _addFileButton = new ToolStripButton();
        _addFolderButton = new ToolStripButton();
        _scanButton = new ToolStripButton();
        _scanStatusLabel = new ToolStripLabel();
        _removeButton = new ToolStripButton();
        _fixAclsButton = new ToolStripButton();
        _applyButton = new Button();
        _exportButton = new ToolStripButton();
        _importButton = new ToolStripButton();
        _progressBar = new ToolStripProgressBar();
        _closeButton = new Button();
        _contextMenuGrants = new ContextMenuStrip(components);
        _ctxAddFile = new ToolStripMenuItem();
        _ctxAddFolder = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();
        _ctxGrantsSep = new ToolStripSeparator();
        _ctxFixAcls = new ToolStripMenuItem();
        _ctxUntrack = new ToolStripMenuItem();
        _ctxGrantsOpenFolderSep = new ToolStripSeparator();
        _ctxOpenFolderGrants = new ToolStripMenuItem();
        _ctxCopyPathGrants = new ToolStripMenuItem();
        _contextMenuTraverse = new ContextMenuStrip(components);
        _ctxTraverseAddFile = new ToolStripMenuItem();
        _ctxTraverseAddFolder = new ToolStripMenuItem();
        _ctxTraverseRemove = new ToolStripMenuItem();
        _ctxTraverseUntrack = new ToolStripMenuItem();
        _ctxTraverseFixAcls = new ToolStripMenuItem();
        _ctxTraverseSep = new ToolStripSeparator();
        _ctxGrantsPropertiesSep = new ToolStripSeparator();
        _ctxPropertiesGrants = new ToolStripMenuItem();
        _ctxTraverseOpenFolderSep = new ToolStripSeparator();
        _ctxTraverseOpenFolder = new ToolStripMenuItem();
        _ctxTraverseCopyPath = new ToolStripMenuItem();
        _ctxTraversePropertiesSep = new ToolStripSeparator();
        _ctxTraverseProperties = new ToolStripMenuItem();

        // tabControl
        _tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _tabControl.Location = new Point(0, 31);
        _tabControl.Size = new Size(760, 420);
        _tabControl.TabIndex = 0;
        _tabControl.Controls.Add(_grantsTab);
        _tabControl.Controls.Add(_traverseTab);
        _tabControl.SelectedIndexChanged += OnTabChanged;

        // grantsTab
        _grantsTab.Text = "Grants";
        _grantsTab.Padding = new Padding(3);
        _grantsTab.Controls.Add(_grantsGrid);

        // traverseTab
        _traverseTab.Text = "Traverse";
        _traverseTab.Padding = new Padding(3);
        _traverseTab.Controls.Add(_traverseGrid);

        // grantsGrid
        _grantsGrid.Dock = DockStyle.Fill;
        _grantsGrid.AllowUserToAddRows = false;
        _grantsGrid.AllowUserToDeleteRows = false;
        _grantsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grantsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grantsGrid.MultiSelect = true;
        _grantsGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grantsGrid.CellValueChanged += OnGrantsCellValueChanged;
        _grantsGrid.CurrentCellDirtyStateChanged += OnGrantsCurrentCellDirtyStateChanged;
        _grantsGrid.SelectionChanged += OnGrantsSelectionChanged;
        _grantsGrid.MouseClick += OnGrantsMouseClick;
        _grantsGrid.KeyDown += OnGridKeyDown;
        _grantsGrid.MouseDown += OnGrantsGridMouseDown;
        _grantsGrid.MouseMove += OnGrantsGridMouseMove;
        _grantsGrid.MouseUp += OnGrantsGridMouseUp;

        // traverseGrid
        _traverseGrid.Dock = DockStyle.Fill;
        _traverseGrid.AllowUserToAddRows = false;
        _traverseGrid.AllowUserToDeleteRows = false;
        _traverseGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _traverseGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _traverseGrid.MultiSelect = true;
        _traverseGrid.ReadOnly = true;
        _traverseGrid.SelectionChanged += OnTraverseSelectionChanged;
        _traverseGrid.MouseClick += OnTraverseMouseClick;
        _traverseGrid.KeyDown += OnGridKeyDown;
        _traverseGrid.MouseDown += OnTraverseGridMouseDown;
        _traverseGrid.MouseMove += OnTraverseGridMouseMove;
        _traverseGrid.MouseUp += OnTraverseGridMouseUp;

        // toolStrip
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addFileButton, _addFolderButton, _scanButton, _removeButton, _fixAclsButton, _exportButton, _importButton, _progressBar, _scanStatusLabel });

        // addFileButton
        _addFileButton.Text = "Add File";
        _addFileButton.Click += OnAddFileClick;

        // addFolderButton
        _addFolderButton.Text = "Add Folder";
        _addFolderButton.Click += OnAddFolderClick;

        // scanButton
        _scanButton.Text = "Scan";
        _scanButton.Click += OnScanFolderClick;

        // scanStatusLabel
        _scanStatusLabel.Text = "";
        _scanStatusLabel.Visible = false;

        // removeButton
        _removeButton.Text = "Delete";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // fixAclsButton
        _fixAclsButton.Text = "Fix ACLs";
        _fixAclsButton.Enabled = false;
        _fixAclsButton.Click += OnFixAclsClick;

        // applyButton
        _applyButton.Text = "Apply";
        _applyButton.Enabled = false;
        _applyButton.FlatStyle = FlatStyle.System;
        _applyButton.Size = new Size(75, 28);
        _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _applyButton.Click += OnApplyClick;

        // exportButton
        _exportButton.Text = "Export";
        _exportButton.Click += OnExportClick;

        // importButton
        _importButton.Text = "Import";
        _importButton.Click += OnImportClick;

        // progressBar
        _progressBar.Visible = false;
        _progressBar.Width = 120;

        // contextMenuGrants
        _ctxAddFile.Text = "Add File";
        _ctxAddFile.Click += OnAddFileClick;
        _ctxAddFolder.Text = "Add Folder";
        _ctxAddFolder.Click += OnAddFolderClick;
        _ctxRemove.Text = "Delete";
        _ctxRemove.Click += OnRemoveClick;
        _ctxUntrack.Text = "Untrack";
        _ctxUntrack.Click += OnUntrackGrantsClick;
        _ctxFixAcls.Text = "Fix ACLs";
        _ctxFixAcls.Click += OnFixAclsClick;
        _ctxOpenFolderGrants.Text = "Open Folder";
        _ctxOpenFolderGrants.Click += OnOpenFolderGrantsClick;
        _ctxCopyPathGrants.Text = "Copy Path";
        _ctxCopyPathGrants.Click += OnCopyPathGrantsClick;
        _ctxPropertiesGrants.Text = "Properties";
        _ctxPropertiesGrants.Click += OnPropertiesGrantsClick;
        _contextMenuGrants.Items.AddRange(new ToolStripItem[] {
            _ctxAddFile, _ctxAddFolder,
            _ctxGrantsSep,
            _ctxRemove, _ctxUntrack, _ctxFixAcls,
            _ctxGrantsOpenFolderSep,
            _ctxOpenFolderGrants, _ctxCopyPathGrants,
            _ctxGrantsPropertiesSep, _ctxPropertiesGrants
        });
        _contextMenuGrants.Opening += OnGrantsContextMenuOpening;

        // contextMenuTraverse
        _ctxTraverseAddFile.Text = "Add File";
        _ctxTraverseAddFile.Click += OnAddTraverseFileClick;
        _ctxTraverseAddFolder.Text = "Add Folder";
        _ctxTraverseAddFolder.Click += OnAddTraverseFolderClick;
        _ctxTraverseRemove.Text = "Delete";
        _ctxTraverseRemove.Click += OnRemoveClick;
        _ctxTraverseUntrack.Text = "Untrack";
        _ctxTraverseUntrack.Click += OnUntrackTraverseClick;
        _ctxTraverseFixAcls.Text = "Fix ACLs";
        _ctxTraverseFixAcls.Click += OnFixAclsClick;
        _ctxTraverseOpenFolder.Text = "Open Folder";
        _ctxTraverseOpenFolder.Click += OnOpenFolderTraverseClick;
        _ctxTraverseCopyPath.Text = "Copy Path";
        _ctxTraverseCopyPath.Click += OnCopyPathTraverseClick;
        _ctxTraverseProperties.Text = "Properties";
        _ctxTraverseProperties.Click += OnPropertiesTraverseClick;
        _contextMenuTraverse.Items.AddRange(new ToolStripItem[] {
            _ctxTraverseAddFile, _ctxTraverseAddFolder,
            _ctxTraverseSep,
            _ctxTraverseRemove, _ctxTraverseUntrack, _ctxTraverseFixAcls,
            _ctxTraverseOpenFolderSep,
            _ctxTraverseOpenFolder, _ctxTraverseCopyPath,
            _ctxTraversePropertiesSep, _ctxTraverseProperties
        });
        _contextMenuTraverse.Opening += OnTraverseContextMenuOpening;

        _grantsGrid.ContextMenuStrip = _contextMenuGrants;
        _traverseGrid.ContextMenuStrip = _contextMenuTraverse;

        // closeButton
        _closeButton.Text = "Close";
        _closeButton.FlatStyle = FlatStyle.System;
        _closeButton.DialogResult = DialogResult.Cancel;
        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _closeButton.Size = new Size(75, 28);

        // Form
        CancelButton = _closeButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 490);
        MinimumSize = new Size(680, 430);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.AddRange(new Control[] { _toolStrip, _tabControl, _applyButton, _closeButton });
        Name = "AclManagerDialog";
        ResumeLayout(false);
    }
}

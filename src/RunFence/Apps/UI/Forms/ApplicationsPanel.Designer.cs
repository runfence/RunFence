#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class ApplicationsPanel
{
    private IContainer components = null;

    private StyledDataGridView _grid;
    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _editButton;
    private ToolStripButton _removeButton;
    private ToolStripButton _launchButton;
    private ToolStripButton _refreshButton;
    private ToolStripSeparator _toolStripSep1;
    private ContextMenuStrip _contextMenu;
    private ContextMenuStrip _headerContextMenu;
    private ToolStripMenuItem _ctxEdit;
    private ToolStripMenuItem _ctxRemove;
    private ToolStripMenuItem _ctxLaunch;
    private ToolStripMenuItem _ctxCopyLauncherPath;
    private ToolStripMenuItem _ctxSaveShortcut;
    private ToolStripMenuItem _ctxCopyPath;
    private ToolStripMenuItem _ctxOpenDir;
    private ToolStripMenuItem _ctxOpenFolder;
    private ToolStripMenuItem _ctxOpenInFolderBrowser;
    private ToolStripMenuItem _ctxGoToAccount;
    private ToolStripMenuItem _ctxSetDefaultBrowser;
    private ToolStripSeparator _ctxSep1;
    private ToolStripSeparator _ctxSep2;
    private ToolStripSeparator _ctxSep3;
    private ToolStripSeparator _ctxSep4;
    private ToolStripMenuItem _hdrAdd;
    private ToolStripButton _associationsButton;
    private ToolStripButton _wizardButton;
    private DataGridViewImageColumn _iconCol;

    private ApplicationsPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gridPopulator?.DisposeFont();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _grid = new StyledDataGridView();
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _editButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _launchButton = new ToolStripButton();
        _refreshButton = new ToolStripButton();
        _toolStripSep1 = new ToolStripSeparator();
        _contextMenu = new ContextMenuStrip();
        _headerContextMenu = new ContextMenuStrip();
        _ctxEdit = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();
        _ctxLaunch = new ToolStripMenuItem();
        _ctxCopyLauncherPath = new ToolStripMenuItem();
        _ctxSaveShortcut = new ToolStripMenuItem();
        _ctxCopyPath = new ToolStripMenuItem();
        _ctxOpenDir = new ToolStripMenuItem();
        _ctxOpenFolder = new ToolStripMenuItem();
        _ctxOpenInFolderBrowser = new ToolStripMenuItem();
        _ctxGoToAccount = new ToolStripMenuItem();
        _ctxSetDefaultBrowser = new ToolStripMenuItem();
        _ctxSep1 = new ToolStripSeparator();
        _ctxSep2 = new ToolStripSeparator();
        _ctxSep3 = new ToolStripSeparator();
        _ctxSep4 = new ToolStripSeparator();
        _hdrAdd = new ToolStripMenuItem();
        _associationsButton = new ToolStripButton();
        _wizardButton = new ToolStripButton();

        ((ISupportInitialize)_grid).BeginInit();
        _toolStrip.SuspendLayout();
        _contextMenu.SuspendLayout();
        _headerContextMenu.SuspendLayout();
        SuspendLayout();

        // _grid columns
        _iconCol = new DataGridViewImageColumn();
        _iconCol.Name = "Icon";
        _iconCol.HeaderText = "";
        _iconCol.Width = 28;
        _iconCol.Resizable = DataGridViewTriState.False;
        _iconCol.SortMode = DataGridViewColumnSortMode.NotSortable;
        _iconCol.ImageLayout = DataGridViewImageCellLayout.Zoom;
        _iconCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns.Add(_iconCol);
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("ExePath", "Path / URL");
        _grid.Columns.Add("Account", "Account");
        _grid.Columns.Add("ACL", "ACL");
        _grid.Columns.Add("Shortcuts", "Shortcuts");

        // _grid
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        _grid.CellMouseClick += OnGridCellMouseClick;
        _grid.KeyDown += OnGridKeyDown;
        _grid.MouseDown += OnGridMouseDown;
        _grid.MouseMove += OnGridMouseMove;
        _grid.MouseUp += OnGridMouseUp;

        // _toolStrip
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(36, 36);
        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _wizardButton, _addButton, _editButton, _removeButton,
            _toolStripSep1,
            _launchButton, _associationsButton, _refreshButton
        });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add";
        _addButton.Click += OnAddClick;

        // _wizardButton
        _wizardButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _wizardButton.ToolTipText = "Setup Wizard";

        // _editButton
        _editButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _editButton.ToolTipText = "Edit";
        _editButton.Enabled = false;
        _editButton.Click += OnEditClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _launchButton
        _launchButton.Text = "Launch";
        _launchButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _launchButton.Enabled = false;
        _launchButton.Click += OnLaunchClick;

        // _associationsButton
        _associationsButton.Text = "Associations";
        _associationsButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _associationsButton.Alignment = ToolStripItemAlignment.Right;
        _associationsButton.Click += OnAssociationsClick;

        // _refreshButton
        _refreshButton.Text = "Reapply ACLs && Shortcuts";
        _refreshButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _refreshButton.Alignment = ToolStripItemAlignment.Right;
        _refreshButton.Click += OnReapplyClick;

        // _contextMenu items
        _ctxEdit.Text = "Edit";
        _ctxEdit.Click += OnEditClick;

        _ctxGoToAccount.Text = "Go to Account";
        _ctxGoToAccount.Click += OnGoToAccountClick;

        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        _ctxLaunch.Text = "Launch";
        _ctxLaunch.Click += OnLaunchClick;

        _ctxOpenFolder.Text = "Open Folder";
        _ctxOpenFolder.Click += OnOpenFolderClick;

        _ctxOpenInFolderBrowser.Text = "Open in Folder Browser";
        _ctxOpenInFolderBrowser.Click += OnOpenInFolderBrowserClick;

        _ctxCopyLauncherPath.Text = "Copy Launcher Command";
        _ctxCopyLauncherPath.Click += OnCopyLauncherPathClick;

        _ctxSaveShortcut.Text = "Save Shortcut...";
        _ctxSaveShortcut.Click += OnSaveShortcutClick;

        _ctxCopyPath.Text = "Copy Path";
        _ctxCopyPath.Click += OnCopyPathClick;

        _ctxOpenDir.Text = "Open Directory";
        _ctxOpenDir.Click += OnOpenDirClick;

        _ctxSetDefaultBrowser.Text = "Default Browser";
        _ctxSetDefaultBrowser.Click += OnSetDefaultBrowserClick;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[]
        {
            _ctxEdit, _ctxGoToAccount, _ctxSep1, _ctxRemove,
            _ctxSep2, _ctxLaunch, _ctxOpenFolder,
            _ctxOpenInFolderBrowser,
            _ctxSep3, _ctxCopyLauncherPath, _ctxSaveShortcut, _ctxCopyPath, _ctxOpenDir,
            _ctxSep4, _ctxSetDefaultBrowser
        });
        _contextMenu.Opening += OnContextMenuOpening;

        // _hdrAdd
        _hdrAdd.Text = "Add";
        _hdrAdd.Click += OnAddClick;

        // _headerContextMenu
        _headerContextMenu.Items.Add(_hdrAdd);

        // ApplicationsPanel
        Dock = DockStyle.Fill;
        Controls.Add(_grid);
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

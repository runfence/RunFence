#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Groups.UI.Forms;

partial class GroupsPanel
{
    private IContainer components = null;

    private ToolStrip _toolStrip;
    private ToolStripButton _refreshButton;
    private ToolStripButton _createGroupButton;
    private ToolStripSeparator _toolStripSep;
    private ToolStripSeparator _toolStripSep2;
    private ToolStripButton _aclManagerButton;
    private ToolStripButton _scanAclsButton;
    private ToolStripButton _accountsButton;
    private ToolStripButton _migrateSidsButton;
    private SplitContainer _splitContainer;
    private StyledDataGridView _groupsGrid;
    private StyledDataGridView _membersGrid;
    private Label _membersHeaderLabel;
    private ToolStrip _membersToolStrip;
    private ToolStripButton _addMemberButton;
    private ToolStripButton _removeMemberButton;
    private Panel _descriptionPanel;
    private TextBox _descriptionTextBox;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxCreateGroup;
    private ToolStripMenuItem _ctxDeleteGroup;
    private ToolStripSeparator _ctxSep1;
    private ToolStripMenuItem _ctxAclManager;
    private ToolStripMenuItem _ctxCopySid;
    private Panel _statusPanel;
    private Label _statusLabel;
    private DataGridViewTextBoxColumn _groupNameCol;
    private DataGridViewTextBoxColumn _groupSidCol;
    private DataGridViewTextBoxColumn _memberNameCol;
    private DataGridViewTextBoxColumn _memberSidCol;

    private GroupsPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _refreshController?.Dispose();
            if (_parentForm != null)
            {
                _parentForm.SizeChanged -= OnParentFormSizeChanged;
                _parentForm = null;
            }
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _toolStrip = new ToolStrip();
        _refreshButton = new ToolStripButton();
        _createGroupButton = new ToolStripButton();
        _toolStripSep = new ToolStripSeparator();
        _toolStripSep2 = new ToolStripSeparator();
        _aclManagerButton = new ToolStripButton();
        _scanAclsButton = new ToolStripButton();
        _accountsButton = new ToolStripButton();
        _migrateSidsButton = new ToolStripButton();
        _splitContainer = new SplitContainer();
        _groupsGrid = new StyledDataGridView();
        _membersGrid = new StyledDataGridView();
        _membersHeaderLabel = new Label();
        _membersToolStrip = new ToolStrip();
        _addMemberButton = new ToolStripButton();
        _removeMemberButton = new ToolStripButton();
        _descriptionPanel = new Panel();
        _descriptionTextBox = new TextBox();
        _contextMenu = new ContextMenuStrip();
        _ctxCreateGroup = new ToolStripMenuItem();
        _ctxDeleteGroup = new ToolStripMenuItem();
        _ctxSep1 = new ToolStripSeparator();
        _ctxAclManager = new ToolStripMenuItem();
        _ctxCopySid = new ToolStripMenuItem();
        _statusLabel = new Label();
        _statusPanel = new Panel();

        ((ISupportInitialize)_splitContainer).BeginInit();
        _splitContainer.Panel1.SuspendLayout();
        _splitContainer.Panel2.SuspendLayout();
        ((ISupportInitialize)_groupsGrid).BeginInit();
        ((ISupportInitialize)_membersGrid).BeginInit();
        _toolStrip.SuspendLayout();
        _membersToolStrip.SuspendLayout();
        _descriptionPanel.SuspendLayout();
        _contextMenu.SuspendLayout();
        SuspendLayout();

        // _groupsGrid columns
        _groupNameCol = new DataGridViewTextBoxColumn();
        _groupNameCol.Name = "Name";
        _groupNameCol.HeaderText = "Group Name";
        _groupNameCol.FillWeight = 40;
        _groupNameCol.ReadOnly = true;

        _groupSidCol = new DataGridViewTextBoxColumn();
        _groupSidCol.Name = "SID";
        _groupSidCol.HeaderText = "SID";
        _groupSidCol.FillWeight = 60;
        _groupSidCol.ReadOnly = true;

        _groupsGrid.Columns.AddRange(new DataGridViewColumn[] { _groupNameCol, _groupSidCol });
        ConfigureReadOnlyGrid(_groupsGrid);
        _groupsGrid.Dock = DockStyle.Fill;
        _groupsGrid.ContextMenuStrip = _contextMenu;
        _groupsGrid.MouseDown += OnGroupsGridMouseDown;

        // _membersGrid columns
        _memberNameCol = new DataGridViewTextBoxColumn();
        _memberNameCol.Name = "MemberName";
        _memberNameCol.HeaderText = "Username";
        _memberNameCol.FillWeight = 50;
        _memberNameCol.ReadOnly = true;

        _memberSidCol = new DataGridViewTextBoxColumn();
        _memberSidCol.Name = "MemberSID";
        _memberSidCol.HeaderText = "SID";
        _memberSidCol.FillWeight = 50;
        _memberSidCol.ReadOnly = true;

        _membersGrid.Columns.AddRange(new DataGridViewColumn[] { _memberNameCol, _memberSidCol });
        ConfigureReadOnlyGrid(_membersGrid);
        _membersGrid.Dock = DockStyle.Fill;

        // _membersHeaderLabel
        _membersHeaderLabel.Text = "Members:";
        _membersHeaderLabel.Dock = DockStyle.Top;
        _membersHeaderLabel.Height = 22;
        _membersHeaderLabel.Padding = new Padding(4, 4, 0, 0);

        // _addMemberButton
        _addMemberButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addMemberButton.ToolTipText = "Add Member";

        // _removeMemberButton
        _removeMemberButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeMemberButton.ToolTipText = "Remove Member";
        _removeMemberButton.Enabled = false;

        // _membersToolStrip
        _membersToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _membersToolStrip.RenderMode = ToolStripRenderMode.System;
        _membersToolStrip.ImageScalingSize = new Size(36, 36);
        _membersToolStrip.Dock = DockStyle.Top;
        _membersToolStrip.Items.AddRange(new ToolStripItem[] { _addMemberButton, _removeMemberButton });

        // _descriptionTextBox
        _descriptionTextBox.Dock = DockStyle.Fill;
        _descriptionTextBox.MaxLength = 512;
        _descriptionTextBox.Multiline = true;
        _descriptionTextBox.ScrollBars = ScrollBars.Vertical;
        _descriptionTextBox.Enabled = false;

        // _descriptionPanel — textbox (fills), then label (topmost)
        _descriptionPanel.Dock = DockStyle.Bottom;
        _descriptionPanel.Height = 56;
        _descriptionPanel.Padding = new Padding(0, 4, 4, 0);
        _descriptionPanel.Controls.Add(_descriptionTextBox);

        // _splitContainer.Panel1 — groups grid (fills)
        _splitContainer.Panel1.Controls.Add(_groupsGrid);

        // _splitContainer.Panel2 — description (bottom), grid (fills), toolbar (top), header label (topmost)
        _splitContainer.Panel2.Controls.Add(_membersGrid);
        _splitContainer.Panel2.Controls.Add(_descriptionPanel);
        _splitContainer.Panel2.Controls.Add(_membersToolStrip);
        _splitContainer.Panel2.Controls.Add(_membersHeaderLabel);

        // _splitContainer
        _splitContainer.Dock = DockStyle.Fill;
        _splitContainer.SplitterDistance = 500;

        // _toolStrip
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(36, 36);
        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _refreshButton, _createGroupButton,
            _toolStripSep, _aclManagerButton, _toolStripSep2, _scanAclsButton,
            _accountsButton, _migrateSidsButton
        });

        // _refreshButton
        _refreshButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _refreshButton.ToolTipText = "Refresh";

        // _createGroupButton
        _createGroupButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _createGroupButton.ToolTipText = "Create Group...";

        // _aclManagerButton
        _aclManagerButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _aclManagerButton.ToolTipText = "ACL Manager";
        _aclManagerButton.Enabled = false;

        // _scanAclsButton
        _scanAclsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _scanAclsButton.ToolTipText = "Scan ACLs";

        // _accountsButton
        _accountsButton.Text = "Accounts Control...";
        _accountsButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _accountsButton.ToolTipText = "Accounts Control";
        _accountsButton.Alignment = ToolStripItemAlignment.Right;

        // _migrateSidsButton
        _migrateSidsButton.Text = "Migrate SIDs...";
        _migrateSidsButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _migrateSidsButton.ToolTipText = "Migrate SIDs...";
        _migrateSidsButton.Alignment = ToolStripItemAlignment.Right;

        // Context menu items
        _ctxCreateGroup.Text = "Create Group...";
        _ctxDeleteGroup.Text = "Delete Group...";
        _ctxAclManager.Text = "ACL Manager";
        _ctxCopySid.Text = "Copy SID";

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[]
        {
            _ctxCreateGroup, _ctxCopySid, _ctxAclManager, _ctxSep1, _ctxDeleteGroup
        });

        // _statusLabel
        _statusLabel.Text = "Ready";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        // _statusPanel
        _statusPanel.Dock = DockStyle.Bottom;
        _statusPanel.Height = 25;
        _statusPanel.Padding = new Padding(5, 2, 5, 2);
        _statusPanel.Controls.Add(_statusLabel);

        // GroupsPanel — splitcontainer (fills), then status (bottom), then toolstrip (top)
        Dock = DockStyle.Fill;
        Controls.Add(_splitContainer);
        Controls.Add(_statusPanel);
        Controls.Add(_toolStrip);

        ((ISupportInitialize)_splitContainer).EndInit();
        _splitContainer.Panel1.ResumeLayout(false);
        _splitContainer.Panel2.ResumeLayout(false);
        ((ISupportInitialize)_groupsGrid).EndInit();
        ((ISupportInitialize)_membersGrid).EndInit();
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _membersToolStrip.ResumeLayout(false);
        _membersToolStrip.PerformLayout();
        _descriptionPanel.ResumeLayout(false);
        _descriptionPanel.PerformLayout();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}

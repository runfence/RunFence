using System.ComponentModel;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Groups.UI.Forms;

public partial class GroupsPanel : DataPanel, IGroupScanProgressPresenter, IGroupSelectionLoadView
{
    private readonly ISystemDialogLauncher _systemDialogLauncher;
    private readonly GroupSidMigrationLauncher _sidMigrationLauncher;
    private readonly IGroupGridPopulator _gridPopulator;
    private readonly GroupActionOrchestrator _contextMenuHandler;
    private readonly GroupMembershipOrchestrator _membershipHandler;
    private readonly GroupRefreshController _refreshController;
    private readonly GroupSelectionLoadController _selectionLoadController;
    private readonly GroupDescriptionEditor _descriptionEditor;
    private readonly GridSortHelper _membersSortHelper = new();
    private readonly ILoggingService _log;
    private bool _splitterInitialized;
    private Form? _parentForm;
    private FormWindowState _lastFormWindowState;
    private bool _isMembersLoading;
    private bool _membersGridFocusedBeforeDisable;

    public event Action? GroupsChanged;

    public GroupsPanel(
        IModalCoordinator modalCoordinator,
        IGroupGridPopulator gridPopulator,
        GroupMembershipOrchestrator membershipOrchestrator,
        GroupActionOrchestrator contextMenuHandler,
        ISystemDialogLauncher systemDialogLauncher,
        GroupSidMigrationLauncher sidMigrationLauncher,
        GroupRefreshController refreshController,
        GroupDescriptionEditor descriptionEditor,
        GroupSelectionLoadController selectionLoadController,
        ILoggingService log)
        : base(modalCoordinator)
    {
        _gridPopulator = gridPopulator;
        _membershipHandler = membershipOrchestrator;
        _contextMenuHandler = contextMenuHandler;
        _systemDialogLauncher = systemDialogLauncher;
        _sidMigrationLauncher = sidMigrationLauncher;
        _refreshController = refreshController;
        _descriptionEditor = descriptionEditor;
        _selectionLoadController = selectionLoadController;
        _log = log;

        InitializeComponent();

        _refreshButton.Image = UiIconFactory.CreateToolbarIcon("\u21BB", Color.FromArgb(0x33, 0x66, 0x99), 48);
        _createGroupButton.Image = UiIconFactory.CreateToolbarIcon("\u271A", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _aclManagerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00), 42);
        _scanAclsButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x22, 0x88, 0x44), 42);
        _accountsButton.Image = UiIconFactory.CreateToolbarIcon("\u2699", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _migrateSidsButton.Image = UiIconFactory.CreateToolbarIcon("\u21C4", Color.FromArgb(0x66, 0x66, 0x99), 42);
        _addMemberButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _removeMemberButton.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 42);

        _ctxAclManager.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00), 16);
        _ctxDeleteGroup.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _ctxCopySid.Image = UiIconFactory.CreateClipboardIcon();

        BuildDynamicContent();
    }

    private void BuildDynamicContent()
    {
        _descriptionEditor.Initialize(_descriptionTextBox);
        _selectionLoadController.Initialize(this);

        _gridPopulator.Initialize(_groupsGrid, _membersGrid, _membersHeaderLabel);
        _scanAclsButton.Visible = _contextMenuHandler.IsBulkScanAvailable;

        _contextMenuHandler.DataChanged += createdSid =>
        {
            if (createdSid != null)
                _gridPopulator.SetPreferredSelection(createdSid);

            GroupsChanged?.Invoke();
            RefreshGridAndDescription();
        };

        _refreshController.Initialize(GetSelectedGroupSid);
        _refreshController.IsRefreshingChanged += _ => UpdateButtonState();
        _refreshController.IsMembersLoadingChanged += isMembersLoading =>
        {
            _isMembersLoading = isMembersLoading;
            UpdateButtonState();
        };
        _refreshController.RefreshCompleted += async info =>
        {
            try
            {
                if (info.SelectedSidAfterRefresh == null)
                {
                    _gridPopulator.ClearMembers();
                }
                else if (!info.MembersWereRefreshed || info.SelectedSidBeforeRefresh != info.SelectedSidAfterRefresh)
                {
                    await RefreshMembersGridAsync(info.SelectedSidAfterRefresh);
                }
            }
            finally
            {
                _isMembersLoading = false;
                UpdateButtonState();
            }

            await _selectionLoadController.LoadDescriptionAfterRefreshAsync(info.SelectedSidAfterRefresh);
        };

        EnableThreeStateSorting(_groupsGrid, RefreshGridAndDescription);
        _membersSortHelper.EnableThreeStateSorting(_membersGrid, BeginRefreshMembersGrid);

        _refreshButton.Click += (_, _) => RefreshGridAndDescription();
        _createGroupButton.Click += (_, _) => _contextMenuHandler.CreateGroup(FindForm());
        _aclManagerButton.Click += (_, _) =>
        {
            var sid = GetSelectedGroupSid();
            if (sid != null)
                _contextMenuHandler.OpenAclManager(sid, FindForm());
        };
        _scanAclsButton.Click += async (_, _) => await _contextMenuHandler.ScanAcls(FindForm()!, this);
        _accountsButton.Click += (_, _) => _systemDialogLauncher.OpenUserAccountsDialog();
        _migrateSidsButton.Click += OnMigrateSidsClick;

        _ctxCreateGroup.Click += (_, _) => _contextMenuHandler.CreateGroup(FindForm());
        _ctxDeleteGroup.Click += (_, _) =>
        {
            var (sid, name) = GetSelectedGroupInfo();
            if (sid != null)
                _contextMenuHandler.DeleteGroup(sid, name!);
        };
        _ctxAclManager.Click += (_, _) =>
        {
            var sid = GetSelectedGroupSid();
            if (sid != null)
                _contextMenuHandler.OpenAclManager(sid, FindForm());
        };
        _ctxCopySid.Click += OnCopySidClick;
        _contextMenu.Opening += OnGroupsContextMenuOpening;

        _groupsGrid.SelectionChanged += OnGroupsGridSelectionChanged;
        _groupsGrid.CellMouseDown += OnGroupsGridCellMouseDown;
        _groupsGrid.CellDoubleClick += OnGroupsGridCellDoubleClick;
        _groupsGrid.KeyDown += OnGroupsGridKeyDown;
        _membersGrid.SelectionChanged += (_, _) => _removeMemberButton.Enabled = _membersGrid.SelectedRows.Count > 0 && !_isMembersLoading;

        _descriptionTextBox.Leave += OnDescriptionLeave;

        _addMemberButton.Click += OnAddMemberClick;
        _removeMemberButton.Click += OnRemoveMemberClick;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _parentForm = FindForm();
        if (_parentForm != null)
        {
            _lastFormWindowState = _parentForm.WindowState;
            _parentForm.SizeChanged += OnParentFormSizeChanged;
            _parentForm.FormClosing += (_, _) => _descriptionEditor.CommitDescription();
        }
    }

    protected override void OnDataSet()
    {
        if (Visible && IsParentFormVisible())
        {
            RefreshGridAndDescription();
            _refreshController.StartRefreshTimer();
        }
    }

    public override void RefreshOnActivation()
    {
        if (_refreshController.IsRefreshing)
            return;

        RefreshGridAndDescription();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsParentFormVisible())
        {
            if (!_splitterInitialized && _splitContainer.Width > 0)
            {
                _splitContainer.SplitterDistance = _splitContainer.Width / 2;
                _splitterInitialized = true;
            }

            _refreshController.StartRefreshTimer();
            RefreshOnActivation();
        }
        else
        {
            _refreshController.StopTimer();
        }
    }

    private void OnParentFormSizeChanged(object? sender, EventArgs e)
    {
        if (_parentForm == null || !Visible)
            return;

        var newState = _parentForm.WindowState;
        var wasMinimized = _lastFormWindowState == FormWindowState.Minimized;
        _lastFormWindowState = newState;
        if (wasMinimized && newState != FormWindowState.Minimized)
            BeginRefreshMembersGrid();
    }

    private async void RefreshGridAndDescription()
    {
        await _refreshController.RefreshNow();
    }

    private bool IsParentFormVisible()
    {
        var form = FindForm();
        return form is { Visible: true } && form.WindowState != FormWindowState.Minimized;
    }

    protected override void UpdateButtonState()
    {
        var hasSelection = _groupsGrid.SelectedRows.Count > 0;
        _aclManagerButton.Enabled = hasSelection;
        _ctxAclManager.Enabled = hasSelection;
        _ctxDeleteGroup.Enabled = hasSelection;
        _ctxCopySid.Enabled = hasSelection;
        _addMemberButton.Enabled = hasSelection && !_isMembersLoading;
        _removeMemberButton.Enabled = _membersGrid.SelectedRows.Count > 0 && !_isMembersLoading;

        var membersEnabled = !_isMembersLoading;
        if (!membersEnabled && _membersGrid.Enabled)
            _membersGridFocusedBeforeDisable = _membersGrid.ContainsFocus;

        _membersGrid.Enabled = membersEnabled;
        if (membersEnabled && _membersGridFocusedBeforeDisable)
        {
            _membersGridFocusedBeforeDisable = false;
            _membersGrid.Focus();
        }
    }

    private Task RefreshMembersGridAsync()
        => RefreshMembersGridAsync(GetSelectedGroupSid());

    internal void BeginRefreshMembersGrid()
    {
        _ = RefreshMembersGridAsync();
    }

    internal async Task RefreshMembersGridAsync(string? sid)
    {
        _isMembersLoading = true;
        UpdateButtonState();

        bool shouldClearMembers = false;
        try
        {
            if (sid == null)
            {
                _gridPopulator.ClearMembers();
                return;
            }

            await _gridPopulator.PopulateMembers(sid);
        }
        catch (Exception ex)
        {
            shouldClearMembers = true;
            _log.Error($"Failed to refresh members for group {sid}", ex);
        }
        finally
        {
            if (!IsDisposed && sid != null && sid == GetSelectedGroupSid() && shouldClearMembers)
                _gridPopulator.ClearMembers();

            if (!IsDisposed && sid == GetSelectedGroupSid())
            {
                _isMembersLoading = false;
                UpdateButtonState();
            }
        }
    }

    private string? GetSelectedGroupSid()
    {
        if (_groupsGrid.SelectedRows.Count == 0)
            return null;

        return _groupsGrid.SelectedRows[0].Tag as string;
    }

    string? IGroupSelectionLoadView.GetSelectedGroupSid() => GetSelectedGroupSid();

    private (string? sid, string? name) GetSelectedGroupInfo()
    {
        if (_groupsGrid.SelectedRows.Count == 0)
            return (null, null);

        var row = _groupsGrid.SelectedRows[0];
        var sid = row.Tag as string;
        var name = row.Cells.Count > 0 ? row.Cells[0].Value as string : sid;
        return (sid, name ?? sid);
    }

    private async void OnGroupsGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_refreshController.IsRefreshing)
            return;

        await _selectionLoadController.HandleSelectionChangedAsync(GetSelectedGroupSid());
    }

    private void OnGroupsGridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;

        _groupsGrid.ClearSelection();
        _groupsGrid.Rows[e.RowIndex].Selected = true;
        _groupsGrid.CurrentCell = _groupsGrid.Rows[e.RowIndex].Cells[0];
    }

    private void OnGroupsGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;

        var hit = _groupsGrid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0)
            _groupsGrid.ClearSelection();
    }

    private void OnGroupsContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var hasSelection = _groupsGrid.SelectedRows.Count > 0;
        _ctxCreateGroup.Visible = !hasSelection;
        _ctxCopySid.Visible = hasSelection;
        _ctxAclManager.Visible = hasSelection;
        _ctxSep1.Visible = hasSelection;
        _ctxDeleteGroup.Visible = hasSelection;
    }

    private void OnGroupsGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || !_contextMenuHandler.IsAclManagerAvailable)
            return;

        var sid = GetSelectedGroupSid();
        if (sid != null)
            _contextMenuHandler.OpenAclManager(sid, FindForm());
    }

    private void OnGroupsGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || _groupsGrid.SelectedRows.Count == 0)
            return;

        _groupsGrid.EndEdit();
        var (sid, name) = GetSelectedGroupInfo();
        if (sid != null)
            _contextMenuHandler.DeleteGroup(sid, name!);
    }

    private void OnCopySidClick(object? sender, EventArgs e)
    {
        var sid = GetSelectedGroupSid();
        if (sid == null)
            return;

        Clipboard.SetText(sid);
    }

    private void OnAddMemberClick(object? sender, EventArgs e)
    {
        var sid = GetSelectedGroupSid();
        if (sid == null)
            return;

        var groupName = _groupsGrid.SelectedRows.Count > 0
            ? _groupsGrid.SelectedRows[0].Cells["GroupName"].Value as string ?? string.Empty
            : string.Empty;
        var existingMemberSids = _membersGrid.Rows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
        if (_membershipHandler.AddMembers(sid, groupName, existingMemberSids, FindForm()))
            BeginRefreshMembersGrid();
    }

    private void OnRemoveMemberClick(object? sender, EventArgs e)
    {
        var groupSid = GetSelectedGroupSid();
        if (groupSid == null || _membersGrid.SelectedRows.Count == 0)
            return;

        var memberSid = _membersGrid.SelectedRows[0].Tag as string ?? string.Empty;
        var memberName = _membersGrid.SelectedRows[0].Cells[0].Value as string ?? memberSid;
        if (_membershipHandler.RemoveMember(groupSid, memberSid, memberName, FindForm()))
            BeginRefreshMembersGrid();
    }

    private void OnDescriptionLeave(object? sender, EventArgs e)
    {
        _descriptionEditor.SaveDescriptionIfChanged();
    }

    private void OnMigrateSidsClick(object? sender, EventArgs e)
    {
        if (_sidMigrationLauncher.Launch(FindForm()))
        {
            GroupsChanged?.Invoke();
            RefreshGridAndDescription();
        }
    }

    public void SetScanBusy(bool busy)
    {
        _scanAclsButton.Enabled = !busy;
    }

    public void SetStatusText(string text)
    {
        _statusLabel.Text = text;
    }

    private void SetMembersLoading(bool isMembersLoading)
    {
        _isMembersLoading = isMembersLoading;
        UpdateButtonState();
    }

    void IGroupSelectionLoadView.SetMembersLoading(bool isMembersLoading) => SetMembersLoading(isMembersLoading);
}

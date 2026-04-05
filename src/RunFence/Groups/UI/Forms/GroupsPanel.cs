using System.ComponentModel;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI.Forms;
using RunFence.UI;
using RunFence.UI.Forms;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Groups.UI.Forms;

public partial class GroupsPanel : DataPanel
{
    private readonly IWindowsAccountService _windowsAccountService;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILoggingService _log;
    private readonly ISidMigrationService _sidMigrationService;
    private readonly Func<InAppMigrationHandler> _createMigrationHandler;
    private readonly ISidResolver _sidResolver;
    private readonly ISidNameCacheService _sidNameCache;
    private readonly GroupGridPopulator _gridPopulator;
    private readonly GroupActionOrchestrator _contextMenuHandler;
    private readonly GroupMembershipOrchestrator _membershipHandler;
    private readonly GridSortHelper _membersSortHelper = new();
    private Timer? _refreshTimer;
    private bool _splitterInitialized;
    private Form? _parentForm;
    private FormWindowState _lastFormWindowState;

    private string? _descriptionGroupSid;
    private string? _originalDescription;
    private bool _isMembersLoading;

    private readonly ILocalGroupMembershipService _groupMembership;

    public event Action? GroupsChanged;

    public GroupsPanel(
        GroupGridPopulator gridPopulator,
        GroupMembershipOrchestrator membershipOrchestrator,
        GroupActionOrchestrator contextMenuHandler,
        ILocalGroupMembershipService groupMembership,
        IWindowsAccountService windowsAccountService,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        ISidResolver sidResolver,
        ISidNameCacheService sidNameCache,
        ISidMigrationService sidMigrationService,
        Func<InAppMigrationHandler> createMigrationHandler)
    {
        _gridPopulator = gridPopulator;
        _membershipHandler = membershipOrchestrator;
        _contextMenuHandler = contextMenuHandler;
        _groupMembership = groupMembership;
        _windowsAccountService = windowsAccountService;
        _localUserProvider = localUserProvider;
        _log = log;
        _sidResolver = sidResolver;
        _sidNameCache = sidNameCache;
        _sidMigrationService = sidMigrationService;
        _createMigrationHandler = createMigrationHandler;

        InitializeComponent();

        // Icons
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
        _gridPopulator.Initialize(_groupsGrid, _membersGrid, _membersHeaderLabel);
        _scanAclsButton.Visible = _contextMenuHandler.IsBulkScanAvailable;

        _contextMenuHandler.DataChanged += () =>
        {
            GroupsChanged?.Invoke();
            RefreshGridAndDescription();
        };

        EnableThreeStateSorting(_groupsGrid, RefreshGridAndDescription);
        _membersSortHelper.EnableThreeStateSorting(_membersGrid, RefreshMembersGrid);

        // Toolbar events
        _refreshButton.Click += (_, _) => RefreshGridAndDescription();
        _createGroupButton.Click += (_, _) => _contextMenuHandler.CreateGroup(FindForm());
        _aclManagerButton.Click += (_, _) =>
        {
            var sid = GetSelectedGroupSid();
            if (sid != null)
                _contextMenuHandler.OpenAclManager(sid, FindForm());
        };
        _scanAclsButton.Click += async (_, _) => await _contextMenuHandler.ScanAcls(FindForm()!, b => _scanAclsButton.Enabled = b, t => _statusLabel.Text = t);
        _accountsButton.Click += (_, _) => _windowsAccountService.OpenUserAccountsDialog();
        _migrateSidsButton.Click += OnMigrateSidsClick;

        // Context menu events
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

        // Grid events — CellMouseDown ensures right-click selects the row before context menu opens
        _groupsGrid.SelectionChanged += OnGroupsGridSelectionChanged;
        _groupsGrid.CellMouseDown += OnGroupsGridCellMouseDown;
        _groupsGrid.CellDoubleClick += OnGroupsGridCellDoubleClick;
        _groupsGrid.KeyDown += OnGroupsGridKeyDown;
        _membersGrid.SelectionChanged += (_, _) => _removeMemberButton.Enabled = _membersGrid.SelectedRows.Count > 0;

        // Description events
        _descriptionTextBox.Leave += OnDescriptionLeave;

        // Members toolbar events
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
            _parentForm.FormClosing += (_, _) => CommitDescription();
        }
    }

    protected override void OnDataSet()
    {
        RefreshGroupsOnly();
        if (_refreshTimer == null)
        {
            _log.Info("GroupsPanel: starting group refresh timer.");
            _refreshTimer = new Timer { Interval = 5000 };
            _refreshTimer.Tick += OnRefreshTimerTick;
            if (Visible && IsParentFormVisible())
                _refreshTimer.Start();
        }
    }

    public override void RefreshOnActivation()
    {
        if (IsRefreshing || _refreshTimer == null)
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

            _refreshTimer?.Start();
            RefreshOnActivation();
        }
        else
            _refreshTimer?.Stop();
    }

    private void OnParentFormSizeChanged(object? sender, EventArgs e)
    {
        if (_parentForm == null || !Visible)
            return;
        var newState = _parentForm.WindowState;
        var wasMinimized = _lastFormWindowState == FormWindowState.Minimized;
        _lastFormWindowState = newState;
        if (wasMinimized && newState != FormWindowState.Minimized)
            RefreshMembersGrid();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!Visible || IsRefreshing)
            return;
        RefreshGroupsOnly();
    }

    private async void RefreshGroupsOnly()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        var sidBeforeRefresh = GetSelectedGroupSid();
        try
        {
            await _gridPopulator.PopulateGroups();
        }
        finally
        {
            IsRefreshing = false;
        }

        UpdateButtonState();
        LoadDescription(GetSelectedGroupSid());
        // If user changed selection during the background refresh, the loaded members are stale — reload
        if (GetSelectedGroupSid() != sidBeforeRefresh)
            RefreshMembersGrid();
    }

    private bool IsParentFormVisible()
    {
        var form = FindForm();
        return form is { Visible: true } && form.WindowState != FormWindowState.Minimized;
    }

    private async void RefreshGridAndDescription()
    {
        await RefreshGridCore();
        LoadDescription(GetSelectedGroupSid());
    }

    private async Task RefreshGridCore()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        var sidBeforeRefresh = GetSelectedGroupSid();
        try
        {
            var groupsTask = _gridPopulator.PopulateGroups();
            var membersTask = sidBeforeRefresh != null
                ? _gridPopulator.PopulateMembers(sidBeforeRefresh)
                : Task.CompletedTask;
            await Task.WhenAll(groupsTask, membersTask);
        }
        finally
        {
            IsRefreshing = false;
        }

        UpdateButtonState();
        // If user changed selection during the background refresh, the loaded members are stale — reload
        if (GetSelectedGroupSid() != sidBeforeRefresh)
            RefreshMembersGrid();
    }

    protected override void UpdateButtonState()
    {
        var hasSelection = _groupsGrid.SelectedRows.Count > 0;
        _aclManagerButton.Enabled = hasSelection;
        _ctxAclManager.Enabled = hasSelection;
        _ctxDeleteGroup.Enabled = hasSelection;
        _ctxCopySid.Enabled = hasSelection;
        _addMemberButton.Enabled = hasSelection && !_isMembersLoading;
    }

    private async void RefreshMembersGrid()
    {
        var sid = GetSelectedGroupSid();
        if (sid == null)
        {
            _gridPopulator.ClearMembers();
            return;
        }

        _isMembersLoading = true;
        UpdateButtonState();
        await _gridPopulator.PopulateMembers(sid);
        if (sid == GetSelectedGroupSid())
        {
            _isMembersLoading = false;
            UpdateButtonState();
        }
    }

    private string? GetSelectedGroupSid()
    {
        if (_groupsGrid.SelectedRows.Count == 0)
            return null;
        return _groupsGrid.SelectedRows[0].Tag as string;
    }

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
        if (IsRefreshing)
            return;

        var sid = GetSelectedGroupSid();

        // Save pending description for the previous group before switching
        CommitDescription();

        // Immediately clear description and members to show clean loading state
        _descriptionGroupSid = sid;
        _originalDescription = null;
        _descriptionTextBox.Text = "";
        _descriptionTextBox.Enabled = false;
        _isMembersLoading = sid != null;
        UpdateButtonState();

        if (sid == null)
        {
            _gridPopulator.ClearMembers();
            return;
        }

        // Load description and members concurrently
        var descTask = Task.Run(() => _groupMembership.GetGroupDescription(sid));
        var membersTask = _gridPopulator.PopulateMembers(sid);

        string? desc = null;
        bool descFailed = false;
        try { desc = await descTask; }
        catch (Exception ex)
        {
            _log.Error($"Failed to load description for group {sid}", ex);
            descFailed = true;
        }

        await membersTask;

        // Only update UI if this group is still selected
        if (sid == GetSelectedGroupSid())
        {
            _originalDescription = descFailed ? null : (desc ?? "").Trim();
            _descriptionTextBox.Text = descFailed ? "" : (desc ?? "");
            _descriptionTextBox.Enabled = !descFailed;
            _isMembersLoading = false;
            UpdateButtonState();
        }
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
        var hasSel = _groupsGrid.SelectedRows.Count > 0;
        _ctxCreateGroup.Visible = !hasSel;
        _ctxCopySid.Visible = hasSel;
        _ctxAclManager.Visible = hasSel;
        _ctxSep1.Visible = hasSel;
        _ctxDeleteGroup.Visible = hasSel;
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
            ? _groupsGrid.SelectedRows[0].Cells["Name"].Value as string ?? ""
            : "";
        var existingMemberSids = _membersGrid.Rows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
        if (_membershipHandler.AddMembers(sid, groupName, existingMemberSids, FindForm()))
            RefreshMembersGrid();
    }

    private void OnRemoveMemberClick(object? sender, EventArgs e)
    {
        var groupSid = GetSelectedGroupSid();
        if (groupSid == null)
            return;
        if (_membersGrid.SelectedRows.Count == 0)
            return;
        var memberSid = _membersGrid.SelectedRows[0].Tag as string ?? "";
        var memberName = _membersGrid.SelectedRows[0].Cells[0].Value as string ?? memberSid;
        if (_membershipHandler.RemoveMember(groupSid, memberSid, memberName, FindForm()))
            RefreshMembersGrid();
    }

    private void LoadDescription(string? groupSid)
    {
        // If the user is actively editing the description for this same group, do not
        // overwrite their in-progress text — the Leave handler will save it when focus moves.
        bool sameGroup = string.Equals(groupSid, _descriptionGroupSid, StringComparison.OrdinalIgnoreCase);
        if (sameGroup && _descriptionTextBox.Focused)
            return;

        SaveDescriptionIfChanged();

        _descriptionGroupSid = groupSid;
        if (groupSid == null)
        {
            _descriptionTextBox.Text = "";
            _descriptionTextBox.Enabled = false;
            _originalDescription = null;
            return;
        }

        try
        {
            var desc = _groupMembership.GetGroupDescription(groupSid) ?? "";
            _originalDescription = desc.Trim();
            _descriptionTextBox.Text = desc;
            _descriptionTextBox.Enabled = true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load description for group {groupSid}", ex);
            _descriptionTextBox.Text = "";
            _descriptionTextBox.Enabled = false;
            _originalDescription = null;
        }
    }

    private void SaveDescriptionIfChanged()
    {
        if (_descriptionTextBox.Focused)
            return;
        CommitDescription();
    }

    private void CommitDescription()
    {
        if (_descriptionGroupSid == null || _originalDescription == null)
            return;
        var current = _descriptionTextBox.Text.Trim();
        if (current == _originalDescription)
            return;

        try
        {
            _groupMembership.UpdateGroupDescription(_descriptionGroupSid, current);
            _originalDescription = current;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to save description for group {_descriptionGroupSid}", ex);
        }
    }

    private void OnDescriptionLeave(object? sender, EventArgs e)
    {
        SaveDescriptionIfChanged();
    }

    private void OnMigrateSidsClick(object? sender, EventArgs e)
    {
        using var dlg = new SidMigrationDialog(Session, _sidMigrationService, _createMigrationHandler(), _localUserProvider, _log, _sidResolver, _sidNameCache);
        ShowModal(dlg, FindForm());
        if (dlg.InAppMigrationApplied)
        {
            GroupsChanged?.Invoke();
            RefreshGridAndDescription();
        }
    }
}
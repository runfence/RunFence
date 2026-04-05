using System.ComponentModel;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Core.Models;
using RunFence.RunAs.UI.Forms;
using RunFence.UI;

namespace RunFence.Acl.UI.Forms;

/// <summary>Result of <see cref="AclConfigSection.BuildResult"/>.</summary>
public record struct AclConfigResult(
    bool RestrictAcl,
    AclMode AclMode,
    AclTarget AclTarget,
    int Depth,
    DeniedRights DeniedRights,
    List<AllowAclEntry>? AllowedEntries);

/// <summary>
/// ACL configuration section for AppEditDialog. Manages mode radios, target radios,
/// folder depth combo, denied rights, allow entries grid, and path conflict detection.
/// </summary>
public partial class AclConfigSection : UserControl
{
    private readonly IAclService _aclService;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILocalGroupMembershipService _groupMembership;
    private readonly SidDisplayNameResolver _displayNameResolver;
    private readonly ISidEntryHelper _sidEntryHelper;
    private readonly AclConfigValidator _validator;

    private readonly List<string> _folderDepthPaths = new();
    private readonly List<int> _folderDepthIndices = new();

    // State
    private bool _isFolder;
    private AclConfigContext? _context;

    /// <summary>Fired when ACL controls change and the parent should re-layout.</summary>
    public event Action? LayoutChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool RestrictAcl
    {
        get => _restrictAclCheckBox.Checked;
        set => _restrictAclCheckBox.Checked = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AclMode AclMode
    {
        get => _aclModeAllowRadio.Checked ? AclMode.Allow : AclMode.Deny;
        set
        {
            _aclModeAllowRadio.Checked = value == AclMode.Allow;
            _aclModeDenyRadio.Checked = value == AclMode.Deny;
        }
    }

    public AclConfigSection(IAclService aclService, ILocalUserProvider localUserProvider,
        ILocalGroupMembershipService groupMembership,
        ISidEntryHelper sidEntryHelper,
        SidDisplayNameResolver displayNameResolver)
    {
        _aclService = aclService;
        _localUserProvider = localUserProvider;
        _groupMembership = groupMembership;
        _sidEntryHelper = sidEntryHelper;
        _displayNameResolver = displayNameResolver;
        _validator = new AclConfigValidator(aclService);
        InitializeComponent();
        _allowTsAddButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _allowTsRemoveButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _allowCtxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        UpdatePanelHeight();
    }

    /// <summary>
    /// Provides context for the section to read current dialog state.
    /// </summary>
    public void SetContext(AclConfigContext context)
    {
        _context = context;
    }

    public void PopulateFromExisting(AppEntry app)
    {
        _restrictAclCheckBox.Checked = app.RestrictAcl;
        _aclModeDenyRadio.Checked = app.AclMode == AclMode.Deny;
        _aclModeAllowRadio.Checked = app.AclMode == AclMode.Allow;
        _deniedRightsComboBox.SelectedIndex = (int)app.DeniedRights;

        if (app.AllowedAclEntries != null)
        {
            foreach (var entry in app.AllowedAclEntries)
            {
                var idx = _allowEntriesGrid.Rows.Add(
                    _displayNameResolver.GetDisplayName(entry.Sid, null, _context?.SidNames),
                    entry.AllowExecute,
                    entry.AllowWrite);
                _allowEntriesGrid.Rows[idx].Tag = entry;
            }
        }

        _aclFileRadio.Checked = app.AclTarget == AclTarget.File;
        _aclFolderRadio.Checked = app.AclTarget == AclTarget.Folder;
    }

    /// <summary>
    /// Updates internal folder state. Called when the file path or folder mode changes.
    /// </summary>
    public void SetExePath(string path, bool isFolder)
    {
        _isFolder = isFolder;

        var isUrl = PathHelper.IsUrlScheme(path);
        Enabled = !isUrl;
        if (isUrl)
            _restrictAclCheckBox.Checked = false;

        if (isFolder)
        {
            _aclFileRadio.Checked = false;
            _aclFolderRadio.Checked = true;
            _aclFileRadio.Enabled = false;
        }
        else
        {
            _aclFileRadio.Enabled = _restrictAclCheckBox.Checked;
        }

        UpdateFolderDepthCombo(path);
        UpdateAclState();
        CheckPathConflict();
    }

    /// <summary>
    /// Refreshes the folder depth combo for the given path.
    /// </summary>
    private void UpdateFolderDepthCombo(string? exePath = null)
    {
        exePath ??= _context?.Provider.GetExePath() ?? "";
        _folderDepthComboBox.Items.Clear();
        _folderDepthPaths.Clear();
        _folderDepthIndices.Clear();

        if (string.IsNullOrEmpty(exePath) || PathHelper.IsUrlScheme(exePath) || (_isFolder ? !Directory.Exists(exePath) : !File.Exists(exePath)))
        {
            _folderDepthComboBox.Items.Add(_isFolder ? "(select folder first)" : "(select file first)");
            _folderDepthComboBox.SelectedIndex = 0;
            return;
        }

        try
        {
            var folder = _isFolder
                ? Path.GetFullPath(exePath)
                : Path.GetDirectoryName(Path.GetFullPath(exePath))!;
            for (int depth = 0; depth <= Constants.MaxFolderAclDepth; depth++)
            {
                if (!_aclService.IsBlockedPath(folder))
                {
                    _folderDepthPaths.Add(folder);
                    _folderDepthIndices.Add(depth);
                    _folderDepthComboBox.Items.Add(Path.GetFileName(folder) is { Length: > 0 } name ? name : folder);
                }

                var parent = Path.GetDirectoryName(folder);
                if (parent == null)
                    break;
                folder = parent;
            }
        }
        catch
        {
            _folderDepthComboBox.Items.Add("(invalid path)");
        }

        if (_folderDepthComboBox.Items.Count > 0)
            _folderDepthComboBox.SelectedIndex = 0;

        UpdateAclPathLabel();
    }

    /// <summary>
    /// Selects the specified folder depth index.
    /// </summary>
    public void SelectFolderDepth(int folderAclDepth)
    {
        var depthIdx = _folderDepthIndices.IndexOf(folderAclDepth);
        if (depthIdx >= 0)
            _folderDepthComboBox.SelectedIndex = depthIdx;
    }

    private (AclTarget Target, int Depth) ResolveAclTargetAndDepth(bool isFolder)
    {
        var aclTarget = isFolder ? AclTarget.Folder : _aclFileRadio.Checked ? AclTarget.File : AclTarget.Folder;
        var depth = _aclFolderRadio.Checked && _folderDepthComboBox.SelectedIndex >= 0 && _folderDepthComboBox.SelectedIndex < _folderDepthIndices.Count
            ? _folderDepthIndices[_folderDepthComboBox.SelectedIndex]
            : 0;
        return (aclTarget, depth);
    }

    private string? CheckPathConflict()
    {
        _allowConflictLabel.Text = "";
        if (!_restrictAclCheckBox.Checked || _context == null)
            return null;

        var exePath = (_context.Provider.GetExePath()).Trim();
        if (string.IsNullOrEmpty(exePath))
            return null;

        var (aclTarget, depth) = ResolveAclTargetAndDepth(_isFolder);
        var isAllowMode = _aclModeAllowRadio.Checked;

        var conflict = _validator.CheckPathConflict(exePath, _isFolder, isAllowMode,
            aclTarget, depth, _context.ExistingApps, _context.CurrentAppId);

        if (conflict != null)
            _allowConflictLabel.Text = conflict;

        _allowConflictLabel.Visible = _restrictAclCheckBox.Checked
                                      && !string.IsNullOrEmpty(_allowConflictLabel.Text);

        return _allowConflictLabel.Text is { Length: > 0 } text ? text : null;
    }

    /// <summary>
    /// Validates ACL settings. Returns error message or null if valid.
    /// </summary>
    public string? Validate(string exePath, bool isFolder)
    {
        var (aclTarget, depth) = ResolveAclTargetAndDepth(isFolder);
        return _validator.Validate(exePath, isFolder, _restrictAclCheckBox.Checked,
            _aclModeAllowRadio.Checked, aclTarget, depth, _allowEntriesGrid.Rows.Count,
            CheckPathConflict);
    }

    /// <summary>
    /// Builds ACL result values from current control state.
    /// </summary>
    public AclConfigResult BuildResult(string exePath, bool isFolder)
    {
        var isUrl = PathHelper.IsUrlScheme(exePath);
        var restrictAcl = _restrictAclCheckBox.Checked && !isUrl;
        var aclMode = _aclModeAllowRadio.Checked ? AclMode.Allow : AclMode.Deny;
        var (aclTarget, depth) = ResolveAclTargetAndDepth(isFolder);
        var deniedRights = (DeniedRights)_deniedRightsComboBox.SelectedIndex;

        List<AllowAclEntry>? allowedEntries = null;
        if (aclMode == AclMode.Allow && restrictAcl)
        {
            allowedEntries = new List<AllowAclEntry>();
            foreach (DataGridViewRow row in _allowEntriesGrid.Rows)
            {
                if (row.Tag is AllowAclEntry entry)
                {
                    allowedEntries.Add(new AllowAclEntry
                    {
                        Sid = entry.Sid,
                        AllowExecute = row.Cells["Execute"].Value is true,
                        AllowWrite = row.Cells["Write"].Value is true
                    });
                }
            }
        }

        return new AclConfigResult(restrictAcl, aclMode, aclTarget, depth, deniedRights, allowedEntries);
    }

    // --- Event handlers (from Designer wiring) ---

    private void OnRestrictAclCheckedChanged(object? sender, EventArgs e)
    {
        UpdateAclState();
        CheckPathConflict();
        UpdatePanelHeight();
        LayoutChanged?.Invoke();
    }

    private void OnAclModeDenyRadioCheckedChanged(object? sender, EventArgs e)
    {
        UpdateAclState();
        CheckPathConflict();
        UpdatePanelHeight();
        LayoutChanged?.Invoke();
        if (_aclModeAllowRadio.Checked && _allowEntriesGrid.Rows.Count == 0)
            PrePopulateAllowListWithSelectedAccount();
    }

    private void OnAclFolderRadioCheckedChanged(object? sender, EventArgs e)
    {
        _folderDepthComboBox.Enabled = _aclFolderRadio.Checked;
        UpdateAclPathLabel();
        CheckPathConflict();
        UpdatePanelHeight();
        LayoutChanged?.Invoke();
    }

    private void OnFolderDepthSelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateAclPathLabel();
        CheckPathConflict();
        UpdatePanelHeight();
        LayoutChanged?.Invoke();
    }

    private void OnAllowGridCurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_allowEntriesGrid.IsCurrentCellDirty)
            _allowEntriesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    // --- Private methods ---

    private void UpdatePanelHeight()
    {
        var isDeny = _aclModeDenyRadio.Checked;
        var isAllow = _aclModeAllowRadio.Checked;
        var isRestricted = _restrictAclCheckBox.Checked;

        _deniedRightsLabel.Visible = isRestricted && isDeny;
        _deniedRightsComboBox.Visible = isRestricted && isDeny;

        _allowPanel.Visible = isRestricted && isAllow;
        _allowConflictLabel.Visible = isRestricted && !string.IsNullOrEmpty(_allowConflictLabel.Text);

        _aclModePanel.Visible = isRestricted;
        _aclTargetPanel.Visible = isRestricted;
        _folderDepthComboBox.Visible = isRestricted;
        _aclPathLabel.Visible = isRestricted;

        bool showConflict = isRestricted && !string.IsNullOrEmpty(_allowConflictLabel.Text);

        // In deny mode, reposition the conflict label below the deny-specific controls.
        if (isDeny && showConflict)
            _allowConflictLabel.Location = _allowConflictLabel.Location with { Y = 182 };
        else if (isAllow)
            _allowConflictLabel.Location = _allowConflictLabel.Location with { Y = 340 };

        int height;
        if (!isRestricted)
            height = 30;
        else if (isAllow)
            height = 368;
        else
            height = showConflict ? 208 : 195;

        Height = height;
    }

    private void UpdateAclState()
    {
        var enabled = _restrictAclCheckBox.Checked;
        _aclModePanel.Enabled = enabled;
        _aclTargetPanel.Enabled = enabled;
        _aclFileRadio.Enabled = enabled && !_isFolder;
        _folderDepthComboBox.Enabled = enabled && _aclFolderRadio.Checked;
        _deniedRightsComboBox.Enabled = enabled && _aclModeDenyRadio.Checked;

        var allowEnabled = enabled && _aclModeAllowRadio.Checked;
        _allowEntriesGrid.Enabled = allowEnabled;
        _allowTsAddButton.Enabled = allowEnabled;
        _allowTsRemoveButton.Enabled = allowEnabled && _allowEntriesGrid.SelectedRows.Count > 0;
    }

    private void UpdateAclPathLabel()
    {
        var idx = _folderDepthComboBox.SelectedIndex;
        if (idx >= 0 && idx < _folderDepthPaths.Count)
            _aclPathLabel.Text = $"Target: {_folderDepthPaths[idx]}";
        else if (_folderDepthComboBox.SelectedItem != null)
            _aclPathLabel.Text = $"Target: {_folderDepthComboBox.SelectedItem}";
    }

    private void PrePopulateAllowListWithSelectedAccount()
    {
        var sid = _context?.Provider.GetSelectedAccountSid();
        if (string.IsNullOrEmpty(sid))
            return;

        // Write access is off by default — app directories rarely need user write access
        // and granting write poses a security risk.
        var entry = new AllowAclEntry { Sid = sid, AllowExecute = true, AllowWrite = false };
        var idx = _allowEntriesGrid.Rows.Add(
            _displayNameResolver.GetDisplayName(sid, null, _context?.SidNames), true, false);
        _allowEntriesGrid.Rows[idx].Tag = entry;

        // Container apps need both the container package SID and the interactive user SID.
        // The container SID was added above (via GetSelectedAccountSid returning the container SID).
        // Now add the interactive user SID so the desktop user token can also reach the exe
        // (AppContainer dual access check: user SID must pass step 1 independently).
        if (_context?.Provider.IsContainerSelected() == true)
        {
            var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
            if (!string.IsNullOrEmpty(interactiveSid) &&
                !string.Equals(interactiveSid, sid, StringComparison.OrdinalIgnoreCase))
            {
                var iEntry = new AllowAclEntry { Sid = interactiveSid, AllowExecute = true, AllowWrite = false };
                var iIdx = _allowEntriesGrid.Rows.Add(
                    _displayNameResolver.GetDisplayName(interactiveSid, null, _context?.SidNames), true, false);
                _allowEntriesGrid.Rows[iIdx].Tag = iEntry;
            }
        }
    }

    private async void OnAllowAddClick(object? sender, EventArgs e)
    {
        var localUsers = _localUserProvider.GetLocalUserAccounts();

        var sid = _context?.Provider.GetSelectedAccountSid();
        if (!string.IsNullOrEmpty(sid))
        {
            var groups = await Task.Run(() => _groupMembership.GetGroupsForUser(sid));
            if (groups.Count > 0)
                localUsers = groups.Concat(localUsers).ToList();
        }

        using var dlg = new CallerIdentityDialog(localUsers, _sidEntryHelper);
        if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
        {
            foreach (DataGridViewRow row in _allowEntriesGrid.Rows)
            {
                if (row.Tag is AllowAclEntry existing &&
                    string.Equals(existing.Sid, dlg.Result, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("This account is already in the list.",
                        "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var entry = new AllowAclEntry { Sid = dlg.Result, AllowExecute = true, AllowWrite = false };

            if (dlg.ResolvedName != null)
                _context?.Provider.OnSidNameLearned(dlg.Result, dlg.ResolvedName);

            var idx = _allowEntriesGrid.Rows.Add(
                _displayNameResolver.GetDisplayName(entry.Sid, null, _context?.SidNames),
                entry.AllowExecute,
                entry.AllowWrite);
            _allowEntriesGrid.Rows[idx].Tag = entry;
        }
    }

    private void OnAllowRemoveClick(object? sender, EventArgs e)
    {
        if (_allowEntriesGrid.SelectedRows.Count > 0)
            _allowEntriesGrid.Rows.RemoveAt(_allowEntriesGrid.SelectedRows[0].Index);
    }

    private void OnAllowSelectionChanged(object? sender, EventArgs e)
    {
        _allowTsRemoveButton.Enabled = _allowEntriesGrid.Enabled && _allowEntriesGrid.SelectedRows.Count > 0;
    }

    private void OnAllowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _allowEntriesGrid.SelectedRows.Count > 0)
            OnAllowRemoveClick(sender, e);
    }

    private void OnAllowGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var hit = _allowEntriesGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0)
            {
                _allowEntriesGrid.ClearSelection();
                _allowEntriesGrid.Rows[hit.RowIndex].Selected = true;
            }
            else
            {
                _allowEntriesGrid.ClearSelection();
            }
        }
    }

    private void OnAllowContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (!_allowEntriesGrid.Enabled)
        {
            e.Cancel = true;
            return;
        }

        var hasSelection = _allowEntriesGrid.SelectedRows.Count > 0;
        _allowCtxAdd.Visible = !hasSelection;
        _allowCtxRemove.Visible = hasSelection;
    }

    private void OnAllowGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        var row = _allowEntriesGrid.Rows[e.RowIndex];
        if (row.Tag is AllowAclEntry entry)
        {
            entry.AllowExecute = row.Cells["Execute"].Value is true;
            entry.AllowWrite = row.Cells["Write"].Value is true;
        }
    }
}
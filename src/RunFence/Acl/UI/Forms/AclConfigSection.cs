using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// ACL configuration section for AppEditDialog. Manages mode radios, target radios,
/// folder depth combo, denied rights, allow entries grid, and path conflict detection.
/// </summary>
public partial class AclConfigSection : UserControl
{
    private readonly IAclService _aclService;
    private readonly AclAllowListGridHandler _allowListHandler;
    private readonly AllowListEntryFactory _allowListEntryFactory;
    private readonly AclConfigValidator _validator;
    private readonly ILoggingService _log;

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

    public AclConfigSection(IAclService aclService, AclAllowListGridHandler allowListHandler, AllowListEntryFactory allowListEntryFactory, AclConfigValidator validator, ILoggingService log)
    {
        _aclService = aclService;
        _allowListHandler = allowListHandler;
        _allowListEntryFactory = allowListEntryFactory;
        _validator = validator;
        _log = log;
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
                    _allowListEntryFactory.GetDisplayName(entry.Sid, null, _context?.SidNames),
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
        catch (Exception ex)
        {
            _log.Debug($"UpdateFolderDepthCombo: path resolution failed for '{exePath}': {ex.Message}");
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

        var isContainer = _context?.Provider.IsContainerSelected() == true;
        var entries = _allowListEntryFactory.BuildPrePopulationEntries(sid, isContainer, _context?.SidNames);

        foreach (var populated in entries)
        {
            var idx = _allowEntriesGrid.Rows.Add(
                populated.DisplayName,
                populated.Entry.AllowExecute,
                populated.Entry.AllowWrite);
            _allowEntriesGrid.Rows[idx].Tag = populated.Entry;
        }
    }

    private async void OnAllowAddClick(object? sender, EventArgs e)
    {
        var existingEntries = _allowEntriesGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Tag is AllowAclEntry)
            .Select(r => (AllowAclEntry)r.Tag!)
            .ToList();

        var result = await _allowListEntryFactory.PromptNewEntryAsync(
            _context?.Provider.GetSelectedAccountSid(),
            _context?.SidNames,
            FindForm(),
            existingEntries);

        if (result == null)
            return;

        if (result.IsDuplicate)
        {
            MessageBox.Show("This account is already in the list.",
                "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (result.ResolvedName != null)
            _context?.Provider.OnSidNameLearned(result.Entry!.Sid, result.ResolvedName);

        var idx = _allowEntriesGrid.Rows.Add(
            result.DisplayName ?? result.Entry!.Sid,
            result.Entry!.AllowExecute,
            result.Entry.AllowWrite);
        _allowEntriesGrid.Rows[idx].Tag = result.Entry;
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
            var rowIndex = _allowListHandler.GetRightClickRowIndex(hit);
            _allowEntriesGrid.ClearSelection();
            if (rowIndex >= 0)
                _allowEntriesGrid.Rows[rowIndex].Selected = true;
        }
    }

    private void OnAllowContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var hasSelection = _allowEntriesGrid.SelectedRows.Count > 0;
        if (!_allowListHandler.BuildContextMenuState(_allowEntriesGrid.Enabled, hasSelection,
                out bool showAdd, out bool showRemove))
        {
            e.Cancel = true;
            return;
        }

        _allowCtxAdd.Visible = showAdd;
        _allowCtxRemove.Visible = showRemove;
    }

    private void OnAllowGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        _allowListHandler.ApplyCellValueToEntry(_allowEntriesGrid.Rows[e.RowIndex]);
    }
}

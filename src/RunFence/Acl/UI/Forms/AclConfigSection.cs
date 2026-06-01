using System.ComponentModel;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// ACL configuration section for AppEditDialog. Manages mode radios, target radios,
/// folder depth combo, denied rights, allow entries grid, and path conflict detection.
/// </summary>
public partial class AclConfigSection : UserControl
{
    private readonly AclAllowListGridHandler _allowListHandler;
    private readonly AllowListEntryFactory _allowListEntryFactory;
    private readonly AclConfigValidator _validator;
    private readonly FolderDepthHelper _folderDepthHelper;

    // State
    private bool _isFolder;
    private string _currentExePath = string.Empty;
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

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AclTarget AclTarget => _aclFileRadio.Checked ? AclTarget.File : AclTarget.Folder;

    public AclConfigSection(AclAllowListGridHandler allowListHandler, AllowListEntryFactory allowListEntryFactory, AclConfigValidator validator, FolderDepthHelper folderDepthHelper)
    {
        _allowListHandler = allowListHandler;
        _allowListEntryFactory = allowListEntryFactory;
        _validator = validator;
        _folderDepthHelper = folderDepthHelper;
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

    public void PopulateFromExisting(AclConfigInitializationModel model)
    {
        _restrictAclCheckBox.Checked = model.RestrictAcl;
        _aclModeDenyRadio.Checked = model.AclMode == AclMode.Deny;
        _aclModeAllowRadio.Checked = model.AclMode == AclMode.Allow;
        _deniedRightsComboBox.SelectedIndex = (int)model.DeniedRights;

        if (model.AllowedAclEntries != null)
        {
            foreach (var entry in model.AllowedAclEntries)
            {
                AddAllowEntry(
                    entry,
                    _allowListEntryFactory.GetDisplayName(entry.Sid, null, _context?.SidNames));
            }
        }

        _aclFileRadio.Checked = model.AclTarget == AclTarget.File;
        _aclFolderRadio.Checked = model.AclTarget == AclTarget.Folder;
    }

    /// <summary>
    /// Updates internal folder state. Called when the file path or folder mode changes.
    /// </summary>
    public void SetExePath(string path, bool isFolder)
    {
        _currentExePath = path;
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
        exePath ??= GetCurrentExePath();
        _folderDepthHelper.UpdateFolderDepthCombo(_folderDepthComboBox, exePath, _isFolder);
    }

    /// <summary>
    /// Selects the specified folder depth index.
    /// </summary>
    public void SelectFolderDepth(int folderAclDepth)
    {
        _folderDepthHelper.SelectFolderDepth(_folderDepthComboBox, folderAclDepth);
    }

    private AclConfigValidationState CheckPathConflict()
    {
        var existingApps = _context?.ExistingApps ?? [];
        return ApplyValidationState(_validator.ValidateState(
            GetCurrentExePath().Trim(),
            _isFolder,
            _restrictAclCheckBox.Checked,
            _aclModeAllowRadio.Checked,
            _aclFileRadio.Checked ? AclTarget.File : AclTarget.Folder,
            _folderDepthHelper.GetSelectedDepth(_folderDepthComboBox.SelectedIndex),
            _allowEntriesGrid.Rows.Cast<DataGridViewRow>().Count(row => row.Tag is AllowAclEntry),
            existingApps,
            _context?.CurrentAppId));
    }

    public AclConfigSectionSnapshot CaptureSnapshot()
    {
        if (_allowEntriesGrid.IsCurrentCellDirty)
            _allowEntriesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _allowEntriesGrid.EndEdit();

        var allowedEntries = _allowEntriesGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Tag is AllowAclEntry)
            .Select(row =>
            {
                var entry = (AllowAclEntry)row.Tag!;
                return new AllowAclEntry
                {
                    Sid = entry.Sid,
                    AllowExecute = row.Cells["Execute"].Value is true,
                    AllowWrite = row.Cells["Write"].Value is true
                };
            })
            .ToList();

        return new AclConfigSectionSnapshot(
            RestrictAcl: _restrictAclCheckBox.Checked,
            AclMode: _aclModeAllowRadio.Checked ? AclMode.Allow : AclMode.Deny,
            SelectedAclTarget: _aclFileRadio.Checked ? AclTarget.File : AclTarget.Folder,
            FolderAclDepth: _folderDepthHelper.GetSelectedDepth(_folderDepthComboBox.SelectedIndex),
            DeniedRights: (DeniedRights)_deniedRightsComboBox.SelectedIndex,
            AllowedEntries: allowedEntries);
    }

    public void RegisterContextHelp(ContextHelpForm host)
    {
        host.SetContextHelp(_aclModeDenyRadio, ContextHelpTextCatalog.AppEdit_Acl_ModeDeny);
        host.SetContextHelp(_aclModeAllowRadio, ContextHelpTextCatalog.AppEdit_Acl_ModeAllow);
        host.SetContextHelp(_deniedRightsComboBox, ContextHelpTextCatalog.AppEdit_Acl_DeniedRights);
    }

    // --- Event handlers (from Designer wiring) ---

    private void OnRestrictAclCheckedChanged(object? sender, EventArgs e)
    {
        UpdateAclState();
        RefreshValidationLayoutAndParent();
    }

    private void OnAclModeDenyRadioCheckedChanged(object? sender, EventArgs e)
    {
        UpdateAclState();
        if (_aclModeAllowRadio.Checked && _allowEntriesGrid.Rows.Count == 0)
            PrePopulateAllowListWithSelectedAccount();
        RefreshValidationLayoutAndParent();
    }

    private void OnAclFolderRadioCheckedChanged(object? sender, EventArgs e)
    {
        _folderDepthComboBox.Enabled = _aclFolderRadio.Checked;
        RefreshValidationLayoutAndParent();
    }

    private void OnFolderDepthSelectedIndexChanged(object? sender, EventArgs e)
    {
        RefreshValidationLayoutAndParent();
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

        _aclModePanel.Visible = isRestricted;
        _aclTargetPanel.Visible = isRestricted;
        _folderDepthComboBox.Visible = isRestricted;
        _aclPathLabel.Visible = isRestricted;

        bool showConflict = isRestricted && !string.IsNullOrEmpty(_allowConflictLabel.Text);

        if (showConflict)
        {
            // Position conflict label below the last visible content control.
            Control lastContent = isDeny ? _deniedRightsComboBox : _allowPanel;
            int conflictY = lastContent.Bottom + 6;
            _allowConflictLabel.Location = _allowConflictLabel.Location with { Y = conflictY };
            _allowConflictLabel.Visible = true;
            Height = _allowConflictLabel.Bottom + 6;
        }
        else
        {
            _allowConflictLabel.Visible = false;
            if (!isRestricted)
                Height = _restrictAclCheckBox.Bottom + 5;
            else if (isAllow)
                Height = _allowPanel.Bottom + 6;
            else
                Height = _deniedRightsComboBox.Bottom + 6;
        }
    }

    private void RefreshValidationLayoutAndParent()
    {
        CheckPathConflict();
        UpdatePanelHeight();
        LayoutChanged?.Invoke();
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

    private AclConfigValidationState ApplyValidationState(AclConfigValidationState state)
    {
        _allowConflictLabel.Text = state.ConflictMessage ?? state.OverlapWarning ?? "";
        _allowConflictLabel.Visible = state.RestrictAcl && !string.IsNullOrEmpty(_allowConflictLabel.Text);
        _aclPathLabel.Text = string.IsNullOrEmpty(state.TargetPath) ? string.Empty : $"Target: {state.TargetPath}";
        return state;
    }

    private string GetCurrentExePath()
    {
        var contextPath = _context?.Provider.GetExePath();
        return contextPath ?? _currentExePath;
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
            AddAllowEntry(populated.Entry, populated.DisplayName);
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
            FindForm()!,
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

        AddAllowEntry(result.Entry!, result.DisplayName ?? result.Entry!.Sid);
        RefreshValidationLayoutAndParent();
    }

    private void OnAllowRemoveClick(object? sender, EventArgs e)
    {
        if (_allowEntriesGrid.SelectedRows.Count > 0)
        {
            _allowEntriesGrid.Rows.RemoveAt(_allowEntriesGrid.SelectedRows[0].Index);
            RefreshValidationLayoutAndParent();
        }
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

    private void AddAllowEntry(AllowAclEntry entry, string displayName)
    {
        var index = _allowEntriesGrid.Rows.Add(displayName, entry.AllowExecute, entry.AllowWrite);
        _allowEntriesGrid.Rows[index].Tag = entry;
    }
}

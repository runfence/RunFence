using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI.Forms;

public partial class GroupMemberPickerDialog : Form
{
    private readonly List<LocalUserAccount> _users;
    private readonly ISidResolver _sidResolver;
    private readonly HashSet<string> _existingMemberSids;
    private readonly string _groupName;

    // Tracks which accounts are checked, including those currently hidden by the filter.
    private readonly HashSet<string> _checkedSids = new(StringComparer.OrdinalIgnoreCase);

    // Tracks the ordered subset of _users currently visible in the list box for index correlation.
    private readonly List<LocalUserAccount> _displayedUsers = new();

    // Guard to suppress ItemCheck events fired by Items.Add during filter rebuilds.
    private bool _applyingFilter;

    public List<LocalUserAccount> SelectedMembers { get; } = new();

    internal GroupMemberPickerDialog(
        ILocalUserProvider localUserProvider,
        ISidResolver sidResolver,
        string groupName,
        HashSet<string> existingMemberSids)
    {
        _sidResolver = sidResolver;
        _existingMemberSids = existingMemberSids;
        _groupName = groupName;

        _users = localUserProvider.GetLocalUserAccounts()
            .Where(u => !existingMemberSids.Contains(u.Sid))
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InitializeComponent();
        Text = $"Add Members to {groupName}";

        if (_users.Count == 0)
        {
            _promptLabel.Text = "All accounts are already members of this group.";
            _membersListBox.Visible = false;
            _searchTextBox.Visible = false;
            _okButton.Enabled = false;
        }
        else
        {
            _promptLabel.Text = $"Select users to add to '{groupName}':";
            ApplyFilter("");
        }
    }

    private void ApplyFilter(string filter)
    {
        _applyingFilter = true;
        try
        {
            _membersListBox.BeginUpdate();
            _membersListBox.Items.Clear();
            _displayedUsers.Clear();

            var filtered = string.IsNullOrEmpty(filter)
                ? _users
                : _users.Where(u => u.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var user in filtered)
            {
                _displayedUsers.Add(user);
                _membersListBox.Items.Add(user.Username, _checkedSids.Contains(user.Sid));
            }

            _membersListBox.EndUpdate();
        }
        finally
        {
            _applyingFilter = false;
        }
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
        => ApplyFilter(_searchTextBox.Text.Trim());

    private void OnMembersListBoxItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_applyingFilter)
            return;
        var user = _displayedUsers[e.Index];
        if (e.NewValue == CheckState.Checked)
            _checkedSids.Add(user.Sid);
        else
            _checkedSids.Remove(user.Sid);
    }

    private void OnAddManuallyClick(object? sender, EventArgs e)
    {
        using var inputDlg = new ManualMemberEntryDialog();
        if (inputDlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(inputDlg.EnteredValue))
            return;

        var input = inputDlg.EnteredValue.Trim();

        string? resolvedSid;
        string? resolvedName;

        if (input.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            // Validate SID format.
            try { _ = new SecurityIdentifier(input); }
            catch
            {
                MessageBox.Show($"'{input}' is not a valid SID.", "Invalid SID",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            resolvedSid = input;
            resolvedName = _sidResolver.TryResolveName(input) ?? input;
        }
        else
        {
            resolvedSid = _sidResolver.TryResolveSid(input);
            if (resolvedSid == null)
            {
                MessageBox.Show($"Could not resolve '{input}' to a known account.", "Resolution Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            resolvedName = _sidResolver.TryResolveName(resolvedSid) ?? input;
        }

        if (_existingMemberSids.Contains(resolvedSid))
        {
            MessageBox.Show($"'{resolvedName}' is already a member of this group.", "Already a Member",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_users.Any(u => string.Equals(u.Sid, resolvedSid, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"'{resolvedName}' is already in the selection list.", "Duplicate",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var account = new LocalUserAccount(resolvedName, resolvedSid);
        _users.Add(account);
        _checkedSids.Add(resolvedSid);

        if (_membersListBox.Visible)
        {
            // Re-apply the current filter so the new entry appears (or stays hidden) correctly.
            ApplyFilter(_searchTextBox.Text.Trim());
        }
        else
        {
            // List was hidden because the initial set was empty; reveal it now.
            _membersListBox.Visible = true;
            _searchTextBox.Visible = true;
            _promptLabel.Text = $"Select users to add to '{_groupName}':";
            ApplyFilter("");
        }

        _okButton.Enabled = true;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        foreach (var user in _users)
        {
            if (_checkedSids.Contains(user.Sid))
                SelectedMembers.Add(user);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

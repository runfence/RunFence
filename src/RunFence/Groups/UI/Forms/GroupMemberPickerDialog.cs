using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI.Forms;

public partial class GroupMemberPickerDialog : Form
{
    private readonly List<LocalUserAccount> _users;

    public List<LocalUserAccount> SelectedMembers { get; } = new();

    internal GroupMemberPickerDialog(
        ILocalUserProvider localUserProvider,
        string groupName,
        HashSet<string> existingMemberSids)
    {
        _users = localUserProvider.GetLocalUserAccounts()
            .Where(u => !existingMemberSids.Contains(u.Sid))
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InitializeComponent();
        Text = $"Add Members to {groupName}";
        _promptLabel.Text = $"Select users to add to '{groupName}':";

        foreach (var user in _users)
            _membersListBox.Items.Add(user.Username);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _membersListBox.Items.Count; i++)
        {
            if (_membersListBox.GetItemChecked(i))
                SelectedMembers.Add(_users[i]);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
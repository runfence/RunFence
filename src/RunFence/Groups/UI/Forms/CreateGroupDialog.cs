using RunFence.Infrastructure;

namespace RunFence.Groups.UI.Forms;

public partial class CreateGroupDialog : Form
{
    private readonly ILocalGroupMembershipService _groupMembership;

    public string? CreatedGroupSid { get; private set; }

    internal CreateGroupDialog(ILocalGroupMembershipService groupMembership)
    {
        _groupMembership = groupMembership;
        InitializeComponent();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _okButton.Enabled = false;
        _statusLabel.Text = "";

        var name = _nameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _statusLabel.Text = "Group name is required.";
            _okButton.Enabled = true;
            return;
        }

        try
        {
            CreatedGroupSid = _groupMembership.CreateGroup(name, null);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            _okButton.Enabled = true;
        }
    }
}
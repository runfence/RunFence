namespace RunFence.Groups.UI;

public sealed class GroupDeletePrompt : IGroupDeletePrompt
{
    public bool ConfirmDelete(string groupName)
    {
        var result = MessageBox.Show(
            $"Delete group '{groupName}'?\n\nThis will remove all ACL grants for this group.",
            "Confirm Delete Group",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        return result == DialogResult.Yes;
    }

    public void ShowDeleteFailed(string message)
    {
        MessageBox.Show(
            message,
            "Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public void ShowSaveFailed(string groupName, string saveErrorMessage)
    {
        MessageBox.Show(
            $"Windows deleted group '{groupName}', but RunFence could not save the cleanup state:\n\n{saveErrorMessage}",
            "Saved State Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

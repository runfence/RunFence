namespace RunFence.Groups.UI;

public class GroupMembershipPrompt : IGroupMembershipPrompt
{
    public bool ConfirmRemove(string memberName)
    {
        var result = MessageBox.Show(
            $"Remove '{memberName}' from this group?",
            "Remove Member", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return result == DialogResult.Yes;
    }

    public void ShowErrors(string title, IReadOnlyList<string> errors)
    {
        MessageBox.Show(string.Join("\n", errors), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
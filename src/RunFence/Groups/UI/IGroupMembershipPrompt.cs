namespace RunFence.Groups.UI;

public interface IGroupMembershipPrompt
{
    bool ConfirmRemove(string memberName);
    void ShowErrors(string title, IReadOnlyList<string> errors);
}
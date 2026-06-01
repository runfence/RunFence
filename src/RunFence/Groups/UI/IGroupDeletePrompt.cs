namespace RunFence.Groups.UI;

public interface IGroupDeletePrompt
{
    bool ConfirmDelete(string groupName);
    void ShowDeleteFailed(string message);
    void ShowSaveFailed(string groupName, string saveErrorMessage);
}

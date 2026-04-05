namespace RunFence.DragBridge;

public interface INotificationService
{
    void ShowInfo(string title, string text);
    void ShowWarning(string title, string text);
    void ShowError(string title, string text);
}
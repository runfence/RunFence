namespace RunFence.DragBridge;

public class NotificationService(NotifyIcon notifyIcon) : INotificationService
{
    public void ShowInfo(string title, string text) =>
        notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    public void ShowWarning(string title, string text) =>
        notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    public void ShowError(string title, string text) =>
        notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Error);
}
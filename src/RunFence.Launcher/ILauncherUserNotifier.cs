namespace RunFence.Launcher;

public interface ILauncherUserNotifier
{
    void ShowError(string message);
    void ShowWarning(string message);
}

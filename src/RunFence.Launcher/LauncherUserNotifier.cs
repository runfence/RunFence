namespace RunFence.Launcher;

public sealed class LauncherUserNotifier : ILauncherUserNotifier
{
    public void ShowError(string message) => LauncherIpcHelper.ShowError(message);
    public void ShowWarning(string message) => LauncherIpcHelper.ShowWarning(message);
}

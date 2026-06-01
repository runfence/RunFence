namespace RunFence.Launcher;

public sealed class LauncherUserNotifier : ILauncherUserNotifier
{
    public void ShowError(string message)
    {
        MessageBox.Show(message, "RunFence Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public void ShowWarning(string message)
    {
        MessageBox.Show(message, "RunFence Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

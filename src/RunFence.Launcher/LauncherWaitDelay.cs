namespace RunFence.Launcher;

public class LauncherWaitDelay : ILauncherWaitDelay
{
    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}

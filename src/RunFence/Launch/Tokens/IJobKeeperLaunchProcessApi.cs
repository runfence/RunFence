namespace RunFence.Launch.Tokens;

public interface IJobKeeperLaunchProcessApi
{
    void AllowAnyForegroundWindow();
    IntPtr OpenLaunchedProcess(int pid);
}

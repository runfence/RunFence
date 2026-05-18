namespace RunFence.Launcher;

public enum LauncherGuiInstanceState
{
    NotRunning,
    RunningInCurrentSession,
    RunningInDifferentSession
}

public interface ILauncherGuiController
{
    LauncherGuiInstanceState GetGuiState();
    bool StartGui(bool grantStartupRunAsUnlock);
}

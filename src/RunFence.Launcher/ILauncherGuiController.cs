namespace RunFence.Launcher;

public interface ILauncherGuiController
{
    bool IsGuiRunning();
    bool StartGui(bool grantStartupRunAsUnlock);
}

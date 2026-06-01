namespace RunFence.Account.UI;

public interface IAccountsPanelTimerCoordinator
{
    event Action? SidChangeDetected;
    event Action? RefreshNeeded;
    void Start();
    void Stop();
    void NotifyVisibilityChanged(bool isVisible);
}

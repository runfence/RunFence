namespace RunFence.Account.UI;

public interface IAccountsPanelProcessRefreshController
{
    void Start(Func<bool> isVisibleAndParentVisible);
    void NotifyParentResized(bool isMinimized);
    void NotifyVisibilityChanged(bool isVisible);
    void TriggerImmediateRefresh();
}

namespace RunFence.Groups.UI;

public interface IGroupScanProgressPresenter
{
    void SetScanBusy(bool busy);
    void SetStatusText(string text);
}

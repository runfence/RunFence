namespace RunFence.Acl.UI;

public interface IAclBulkScanMessagePresenter
{
    void ShowNoKnownSids(IWin32Window? owner, string message);
    void ShowNoResults(IWin32Window? owner, string message);
    void ShowScanFailed(IWin32Window? owner, Exception exception);
}

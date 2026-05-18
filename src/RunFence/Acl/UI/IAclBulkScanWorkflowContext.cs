namespace RunFence.Acl.UI;

public interface IAclBulkScanWorkflowContext
{
    IWin32Window? Owner { get; }
    Task<HashSet<string>> GetKnownSidsAsync();
    void SetScanBusy(bool busy);
    void SetStatusText(string text);
    DialogResult ShowResults(Form dialog);
    void SaveImportedResults();
    void ShowNoKnownSids();
    void ShowNoResults();
    void ShowScanFailed(Exception exception);
    string FailureLogMessage { get; }
}

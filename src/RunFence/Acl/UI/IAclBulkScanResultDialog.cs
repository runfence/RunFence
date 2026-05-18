namespace RunFence.Acl.UI;

public interface IAclBulkScanResultDialog : IDisposable
{
    Form Form { get; }
    Dictionary<string, AccountScanResult> SelectedResults { get; }
}

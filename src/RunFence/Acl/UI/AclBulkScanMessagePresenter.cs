namespace RunFence.Acl.UI;

public sealed class AclBulkScanMessagePresenter : IAclBulkScanMessagePresenter
{
    public void ShowNoKnownSids(IWin32Window? owner, string message)
    {
        MessageBox.Show(owner, message, "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void ShowNoResults(IWin32Window? owner, string message)
    {
        MessageBox.Show(owner, message, "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void ShowScanFailed(IWin32Window? owner, Exception exception)
    {
        MessageBox.Show(owner, $"Scan failed: {exception.Message}", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

namespace RunFence.Acl.UI;

public sealed class AclBulkScanWarningPresenter : IAclBulkScanWarningPresenter
{
    public void ShowSkippedConflictWarning(AclBulkScanImportSummary summary, string title)
    {
        var message = AclBulkScanWarningMessage.BuildSkippedConflictWarningMessage(summary);
        if (message == null)
            return;
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

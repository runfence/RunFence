using System.Windows.Forms;

namespace RunFence.Acl.UI;

public sealed class AclManagerCloseCoordinator(
    AclManagerPendingChanges pending,
    AclManagerScanCancellationController scanCancellation)
{
    public void ApplyCloseDecision(FormClosingEventArgs e, bool cancelClose)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (cancelClose)
        {
            e.Cancel = true;
            return;
        }

        scanCancellation.CancelActiveScan();
        if (pending.HasPendingChanges)
            pending.Clear();
    }
}

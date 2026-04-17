namespace RunFence.Firewall.UI;

/// <summary>
/// Event args for the <see cref="Forms.FirewallAllowlistDialog.Applied"/> event.
/// Subscribers that perform a rollback must set <see cref="RolledBack"/> to <c>true</c>
/// so the dialog keeps the Apply button enabled for a retry.
/// </summary>
public class FirewallApplyEventArgs : EventArgs
{
    public bool RolledBack { get; set; }
}

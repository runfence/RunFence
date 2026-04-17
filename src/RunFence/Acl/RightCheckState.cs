namespace RunFence.Acl;

/// <summary>
/// Three-state check value for rights display in the service layer.
/// Mirrors <see cref="System.Windows.Forms.CheckState"/> without a WinForms dependency.
/// Direct ACE for this SID → Checked. Group ACE only → Indeterminate. No ACE → Unchecked.
/// </summary>
public enum RightCheckState
{
    Unchecked = 0,
    Checked = 1,
    Indeterminate = 2
}

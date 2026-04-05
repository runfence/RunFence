namespace RunFence.Acl.UI;

/// <summary>
/// Abstracts the hosting dialog for ACL Manager modification operations,
/// allowing handlers to toggle enabled state and cursor without depending on
/// <see cref="RunFence.Acl.UI.Forms.AclManagerDialog"/> directly.
/// </summary>
public interface IAclManagerDialogHost : IWin32Window
{
    bool Enabled { get; set; }
    Cursor Cursor { get; set; }
    bool IsDisposed { get; }
}
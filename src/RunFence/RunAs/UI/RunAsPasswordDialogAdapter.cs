using System.Windows.Forms;
using RunFence.Core;
using RunFence.RunAs.UI.Forms;

namespace RunFence.RunAs.UI;

public sealed class RunAsPasswordDialogAdapter(RunAsPasswordDialog dialog) : IRunAsPasswordDialogAdapter
{
    public ProtectedString? Password => dialog.Password;

    public bool RememberPassword => dialog.RememberPassword;

    public DialogResult ShowDialog(IWin32Window? owner) => dialog.ShowDialog(owner);

    public void Dispose() => dialog.Dispose();
}

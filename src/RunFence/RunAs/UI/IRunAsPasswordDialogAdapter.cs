using System.Windows.Forms;
using RunFence.Core;

namespace RunFence.RunAs.UI;

public interface IRunAsPasswordDialogAdapter : IDisposable
{
    ProtectedString? Password { get; }
    bool RememberPassword { get; }
    DialogResult ShowDialog(IWin32Window? owner);
}

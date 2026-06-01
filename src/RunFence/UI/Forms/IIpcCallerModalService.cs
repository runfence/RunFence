using System.Windows.Forms;
using RunFence.Core.Models;

namespace RunFence.UI.Forms;

public interface IIpcCallerModalService
{
    CallerIdentitySelectionResult PromptForCallerIdentity(IWin32Window? owner, IReadOnlyList<LocalUserAccount> localUsers, ISidEntryHelper sidEntryHelper);
    void ShowDuplicateCallerWarning(IWin32Window? owner);
}

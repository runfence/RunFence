using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public class MemberPickerDialogAdapter(ILocalUserProvider localUserProvider, ISidResolver sidResolver, IModalCoordinator modalCoordinator) : IMemberPickerDialog
{
    public List<LocalUserAccount>? ShowPicker(string groupName, HashSet<string> existingMemberSids, IWin32Window? owner)
    {
        using var dlg = new GroupMemberPickerDialog(localUserProvider, sidResolver, groupName, existingMemberSids);
        if (modalCoordinator.ShowModal(dlg, owner) != DialogResult.OK || dlg.SelectedMembers.Count == 0)
            return null;
        return dlg.SelectedMembers;
    }
}

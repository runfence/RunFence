using RunFence.Core.Models;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;
using RunFence.UI.Forms;

namespace RunFence.Groups.UI;

public class MemberPickerDialogAdapter(ILocalUserProvider localUserProvider) : IMemberPickerDialog
{
    public List<LocalUserAccount>? ShowPicker(string groupName, HashSet<string> existingMemberSids, IWin32Window? owner)
    {
        using var dlg = new GroupMemberPickerDialog(localUserProvider, groupName, existingMemberSids);
        if (DataPanel.ShowModal(dlg, owner) != DialogResult.OK || dlg.SelectedMembers.Count == 0)
            return null;
        return dlg.SelectedMembers;
    }
}
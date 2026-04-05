using RunFence.Core.Models;

namespace RunFence.Groups.UI;

public interface IMemberPickerDialog
{
    List<LocalUserAccount>? ShowPicker(string groupName, HashSet<string> existingMemberSids, IWin32Window? owner);
}
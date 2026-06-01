using System.Windows.Forms;
using RunFence.Core.Models;

namespace RunFence.Groups.UI;

public interface IGroupGridPopulator
{
    IReadOnlyList<LocalUserAccount> LastGroups { get; }

    void Initialize(DataGridView groupsGrid, DataGridView membersGrid, Label membersHeaderLabel);
    void SetPreferredSelection(string? sid);
    Task PopulateGroups();
    Task PopulateMembers(string groupSid);
    void ClearMembers();
}

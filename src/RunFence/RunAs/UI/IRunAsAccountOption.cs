using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public interface IRunAsAccountOption
{
    string DisplayName { get; }
    bool IsSelectable { get; }
    PrivilegeLevel AccountPrivilegeLevel { get; }
    AppEntry? ExistingAppForSelection { get; }
    bool SuggestsBasicPrivilegeLevel { get; }
}

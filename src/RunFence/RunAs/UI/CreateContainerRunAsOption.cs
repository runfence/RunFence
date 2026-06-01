using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record CreateContainerRunAsOption(
    string DisplayName,
    bool IsSelectable,
    AppEntry? ExistingAppForSelection) : IRunAsAccountOption
{
    public PrivilegeLevel AccountPrivilegeLevel => PrivilegeLevel.LowIntegrity;

    public bool SuggestsBasicPrivilegeLevel => false;
}

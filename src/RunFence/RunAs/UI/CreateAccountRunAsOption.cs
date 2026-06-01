using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record CreateAccountRunAsOption(
    string DisplayName,
    bool IsSelectable,
    PrivilegeLevel AccountPrivilegeLevel,
    AppEntry? ExistingAppForSelection,
    bool SuggestsBasicPrivilegeLevel) : IRunAsAccountOption;

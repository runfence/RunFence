using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record AppContainerRunAsOption(
    AppContainerEntry Container,
    string Sid,
    string ContainerName,
    string DisplayName,
    bool IsSelectable,
    PrivilegeLevel AccountPrivilegeLevel,
    AppEntry? ExistingAppForSelection,
    bool SuggestsBasicPrivilegeLevel) : IRunAsAccountOption;

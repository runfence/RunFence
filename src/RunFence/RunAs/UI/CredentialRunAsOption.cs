using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record CredentialRunAsOption(
    CredentialEntry Credential,
    string Sid,
    string DisplayName,
    bool IsCurrentAccount,
    bool IsSelectable,
    PrivilegeLevel AccountPrivilegeLevel,
    AppEntry? ExistingAppForSelection,
    bool SuggestsBasicPrivilegeLevel) : IRunAsAccountOption;

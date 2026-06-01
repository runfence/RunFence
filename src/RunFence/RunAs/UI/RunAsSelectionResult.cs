using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed record RunAsSelectionResult(
    int SelectedIndex,
    bool IsSelectionAllowed,
    PrivilegeLevel PrivilegeLevel,
    bool PrivilegeSelectionEnabled,
    string AddAppButtonText,
    bool AddAppButtonEnabled,
    AppEntry? ExistingAppForSelection);

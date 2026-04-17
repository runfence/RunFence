using RunFence.Core.Models;

namespace RunFence.Apps.UI.Forms;

/// <summary>
/// Optional initial values for a new app entry — used when opening the dialog from an account
/// or from the RunAs flow with pre-populated fields.
/// </summary>
public record AppEditDialogOptions(
    string? ConfigPath = null,
    string? ExePath = null,
    string? AccountSid = null,
    string? ContainerName = null,
    bool RestrictAcl = false,
    bool ManageShortcuts = false,
    PrivilegeLevel? PrivilegeLevel = null,
    bool LaunchNow = false);

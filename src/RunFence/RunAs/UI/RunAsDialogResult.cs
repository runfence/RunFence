using System.Security;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.RunAs.UI.Forms;

namespace RunFence.RunAs.UI;

/// <summary>
/// Captures the result state of a <see cref="RunAsDialog"/> after the user confirms.
/// </summary>
public record RunAsDialogResult(
    CredentialEntry? Credential,
    AppContainerEntry? SelectedContainer,
    AncestorPermissionResult? PermissionGrant,
    bool CreateAppEntryOnly,
    PrivilegeLevel PrivilegeLevel,
    bool UpdateOriginalShortcut,
    bool RevertShortcutRequested,
    AppEntry? EditExistingApp,
    AppEntry? ExistingAppForLaunch,
    SecureString? AdHocPassword = null,
    bool RememberPassword = false) : IDisposable
{
    public void Dispose() => AdHocPassword?.Dispose();

    /// <summary>Returns an empty result representing a declined or no-op dialog outcome.</summary>
    public static RunAsDialogResult Empty() => new(
        Credential: null,
        SelectedContainer: null,
        PermissionGrant: null,
        CreateAppEntryOnly: false,
        PrivilegeLevel: PrivilegeLevel.Basic,
        UpdateOriginalShortcut: false,
        RevertShortcutRequested: false,
        EditExistingApp: null,
        ExistingAppForLaunch: null);
}
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
    bool LaunchAsLowIntegrity,
    bool LaunchAsSplitToken,
    bool UpdateOriginalShortcut,
    bool RevertShortcutRequested,
    AppEntry? EditExistingApp,
    AppEntry? ExistingAppForLaunch,
    SecureString? AdHocPassword = null,
    bool RememberPassword = false) : IDisposable
{
    public void Dispose() => AdHocPassword?.Dispose();
}
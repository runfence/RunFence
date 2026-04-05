using RunFence.Core.Models;

namespace RunFence.Apps;

/// <summary>
/// Stateless helpers for common AppEntry UI operations.
/// </summary>
public static class AppEntryHelper
{
    /// <summary>
    /// Builds the confirmation message shown before removing an app entry.
    /// Includes an ACL warning when the app uses allow-mode ACL restriction.
    /// </summary>
    public static string GetRemoveConfirmationMessage(AppEntry app)
    {
        return app is { RestrictAcl: true, AclMode: AclMode.Allow }
            ? $"Remove '{app.Name}'?\n\nWarning: The allow-mode ACL on this path will be reverted (inheritance re-enabled). The original ACL cannot be restored."
            : $"Remove '{app.Name}'?";
    }
}
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps;

/// <summary>
/// Stateless helpers for common AppEntry UI operations.
/// </summary>
public static class AppEntryHelper
{
    public static IReadOnlyCollection<string> BrowserKeys => Constants.BrowserAssociations;

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

    /// <summary>
    /// Returns true when <paramref name="appId"/> has all four browser handler keys
    /// (http, https, .htm, .html) registered in <paramref name="allMappings"/>.
    /// </summary>
    public static bool IsDefaultBrowser(string appId, IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> allMappings)
        => BrowserKeys.All(key =>
            allMappings.TryGetValue(key, out var entries) &&
            entries.Any(e => string.Equals(e.AppId, appId, StringComparison.Ordinal)));
}
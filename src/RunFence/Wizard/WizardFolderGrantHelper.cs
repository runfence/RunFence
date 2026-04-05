using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;

namespace RunFence.Wizard;

/// <summary>
/// Shared folder-grant logic for wizard templates.
/// Grants file system rights on a set of paths for an account SID and records
/// the chosen <see cref="SavedRightsState"/> on the resulting grant entry.
/// </summary>
public class WizardFolderGrantHelper(IPermissionGrantService permissionGrantService, IQuickAccessPinService quickAccessPinService)
{
    /// <summary>
    /// Grants <paramref name="rights"/> on each path in <paramref name="paths"/> for
    /// <paramref name="sid"/>, updates the matching <see cref="GrantedPathEntry.SavedRights"/>,
    /// and reports progress/errors via <paramref name="progress"/>.
    /// </summary>
    public async Task GrantFolderAccessAsync(
        IEnumerable<string> paths,
        string sid,
        FileSystemRights rights,
        SavedRightsState savedRights,
        SessionContext session,
        IWizardProgressReporter progress)
    {
        var pinPaths = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            try
            {
                progress.ReportStatus($"Granting access to {Path.GetFileName(path)}...");
                await Task.Run(() => permissionGrantService.EnsureAccess(path, sid, rights));

                var account = session.Database.GetOrCreateAccount(sid);
                var normalized = Path.GetFullPath(path);
                var grantEntry = account.Grants.LastOrDefault(g =>
                    string.Equals(g.Path, normalized, StringComparison.OrdinalIgnoreCase)
                    && g is { IsDeny: false, IsTraverseOnly: false });
                if (grantEntry != null)
                {
                    grantEntry.SavedRights = savedRights;
                    if (Directory.Exists(normalized))
                        pinPaths.Add(normalized);
                }
            }
            catch (Exception ex)
            {
                progress.ReportError($"Access grant for {path}: {ex.Message}");
            }
        }

        if (pinPaths.Count > 0)
            quickAccessPinService.PinFolders(sid, pinPaths);
    }
}

using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;

namespace RunFence.Wizard;

/// <summary>
/// Shared folder-grant logic for wizard templates.
/// Grants file system rights on a set of paths for an account SID and records
/// the chosen <see cref="SavedRightsState"/> on the resulting grant entry.
/// </summary>
public class WizardFolderGrantHelper(IPathGrantService pathGrantService, IQuickAccessPinService quickAccessPinService)
{
    /// <summary>
    /// Grants <paramref name="savedRights"/> on each path in <paramref name="paths"/> for
    /// <paramref name="sid"/> and reports progress/errors via <paramref name="progress"/>.
    /// </summary>
    public async Task GrantFolderAccessAsync(
        IEnumerable<string> paths,
        string sid,
        SavedRightsState savedRights,
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
                var result = await Task.Run(() => pathGrantService.EnsureAccess(sid, path, savedRights));

                if (result.GrantAdded)
                {
                    var normalized = Path.GetFullPath(path);
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

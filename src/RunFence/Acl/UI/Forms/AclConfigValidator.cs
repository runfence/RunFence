using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Validates ACL configuration settings and detects path conflicts between apps.
/// Extracted from <see cref="AclConfigSection"/> to keep the section focused on UI.
/// </summary>
public class AclConfigValidator(IAclService aclService, ILoggingService log)
{
    /// <summary>
    /// Validates ACL settings for the given path and mode. Returns an error message or null if valid.
    /// </summary>
    public string? Validate(string exePath, bool isFolder, bool restrictAcl, bool isAllowMode,
        AclTarget aclTarget, int depth, int allowEntryCount, Func<string?> checkPathConflict)
    {
        if (!restrictAcl || PathHelper.IsUrlScheme(exePath))
            return null;

        var targetPath = aclService.ResolveAclTargetPath(new AppEntry
        {
            ExePath = exePath, IsFolder = isFolder, AclTarget = aclTarget, FolderAclDepth = depth
        });
        if (aclService.IsBlockedPath(targetPath))
            return $"Cannot restrict access on: {targetPath}";

        var conflict = checkPathConflict();
        if (conflict != null)
            return conflict;

        if (isAllowMode && allowEntryCount == 0)
            return "Allow mode requires at least one entry.";

        return null;
    }

    /// <summary>
    /// Checks for path conflicts between the current app config and existing apps.
    /// Returns the conflict message or null if no conflict.
    /// </summary>
    public string? CheckPathConflict(string exePath, bool isFolder, bool isAllowMode,
        AclTarget aclTarget, int depth, List<AppEntry> existingApps, string? currentAppId)
    {
        if (string.IsNullOrEmpty(exePath) || PathHelper.IsUrlScheme(exePath))
            return null;

        string targetPath;
        try
        {
            targetPath = aclService.ResolveAclTargetPath(new AppEntry
            {
                ExePath = exePath, IsFolder = isFolder, AclTarget = aclTarget, FolderAclDepth = depth
            });
        }
        catch (Exception ex)
        {
            log.Debug($"CheckPathConflict: failed to resolve target path for '{exePath}': {ex.Message}");
            return null;
        }

        var normalizedTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);

        foreach (var other in existingApps)
        {
            if (other.Id == (currentAppId ?? ""))
                continue;
            if (!other.RestrictAcl || other.IsUrlScheme)
                continue;

            string otherTarget;
            try
            {
                otherTarget = aclService.ResolveAclTargetPath(other);
            }
            catch (Exception ex)
            {
                log.Debug($"CheckPathConflict: failed to resolve target path for app '{other.Name}': {ex.Message}");
                continue;
            }

            var normalizedOther = Path.GetFullPath(otherTarget).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(normalizedTarget, normalizedOther, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isAllowMode || other.AclMode == AclMode.Allow)
                return $"Another app ({other.Name}) already manages ACLs on this path.";
        }

        return null;
    }
}

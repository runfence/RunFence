using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Validates ACL configuration settings and detects path conflicts between apps.
/// Extracted from <see cref="AclConfigSection"/> to keep the section focused on UI.
/// </summary>
public class AclConfigValidator(IAclService aclService, ILoggingService log)
{
    /// <summary>
    /// Validates ACL settings for the given path and mode. Returns an error message or null if valid.
    /// Blocking errors: blocked path, exact-path conflict (same path, mixed Allow/Deny), missing allow entries.
    /// Parent-child overlap warnings are non-blocking; they are shown in the UI label but do not prevent save.
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
    /// Checks for exact-path conflicts between the current app config and existing apps.
    /// Returns a blocking error message or null if no error conflict.
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

        var normalizedTarget = AclHelper.NormalizePath(targetPath);

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

            var normalizedOther = AclHelper.NormalizePath(otherTarget);
            if (!string.Equals(normalizedTarget, normalizedOther, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isAllowMode || other.AclMode == AclMode.Allow)
                return $"Another app ({other.Name}) already manages ACLs on this path.";
        }

        return null;
    }

    /// <summary>
    /// Checks for parent-child path overlaps where Deny takes precedence over Allow, making the Allow
    /// ineffective. Returns a non-blocking warning message or null if no overlap.
    /// </summary>
    public string? CheckPathOverlapWarning(string exePath, bool isFolder, bool isAllowMode,
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
            log.Debug($"CheckPathOverlapWarning: failed to resolve target path for '{exePath}': {ex.Message}");
            return null;
        }

        var normalizedTarget = AclHelper.NormalizePath(targetPath);

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
                log.Debug($"CheckPathOverlapWarning: failed to resolve target path for app '{other.Name}': {ex.Message}");
                continue;
            }

            var normalizedOther = AclHelper.NormalizePath(otherTarget);

            // Skip exact matches — handled as errors in CheckPathConflict
            if (string.Equals(normalizedTarget, normalizedOther, StringComparison.OrdinalIgnoreCase))
                continue;

            bool targetIsParentOfOther = normalizedOther.StartsWith(
                normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            bool otherIsParentOfTarget = normalizedTarget.StartsWith(
                normalizedOther + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            if (targetIsParentOfOther && !isAllowMode && other.AclMode == AclMode.Allow)
                return $"Warning: this app's Deny path is a parent of {other.Name}'s Allow path. " +
                       $"The Deny rule will take precedence and make the Allow ineffective.";
            if (otherIsParentOfTarget && other.AclMode == AclMode.Deny && isAllowMode)
                return $"Warning: {other.Name}'s Deny path is a parent of this app's Allow path. " +
                       $"The Deny rule will take precedence and make the Allow ineffective.";
        }

        return null;
    }
}

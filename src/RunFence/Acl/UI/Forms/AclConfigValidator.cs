using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Validates ACL configuration settings and detects path conflicts between apps.
/// Extracted from <see cref="AclConfigSection"/> to keep the section focused on UI.
/// </summary>
public class AclConfigValidator(IAclService aclService, ILoggingService log)
{
    public AclConfigValidationState ValidateState(
        string exePath,
        bool isFolder,
        bool restrictAcl,
        bool isAllowMode,
        AclTarget aclTarget,
        int depth,
        int allowEntryCount,
        List<AppEntry> existingApps,
        string? currentAppId)
    {
        var selectedTarget = isFolder ? AclTarget.Folder : aclTarget;
        var folderDepth = selectedTarget == AclTarget.Folder ? depth : 0;
        var aclMode = isAllowMode ? AclMode.Allow : AclMode.Deny;
        var normalizedRestrictAcl = restrictAcl && !PathHelper.IsUrlScheme(exePath);
        var targetPath = TryResolveTargetPath(exePath, isFolder, selectedTarget, folderDepth);
        var conflict = normalizedRestrictAcl
            ? CheckPathConflict(targetPath, isAllowMode, existingApps, currentAppId)
            : null;
        var overlapWarning = normalizedRestrictAcl && conflict == null
            ? CheckPathOverlapWarning(targetPath, isAllowMode, existingApps, currentAppId)
            : null;
        var error = Validate(targetPath, normalizedRestrictAcl, isAllowMode, allowEntryCount, conflict);

        return new AclConfigValidationState(
            IsValid: error == null,
            SelectedAclTarget: selectedTarget,
            FolderAclDepth: folderDepth,
            AclMode: aclMode,
            IsAllowMode: isAllowMode,
            RestrictAcl: normalizedRestrictAcl,
            ConflictMessage: error,
            OverlapWarning: overlapWarning,
            TargetPath: targetPath);
    }

    private string? CheckPathConflict(
        string? targetPath,
        bool isAllowMode,
        List<AppEntry> existingApps,
        string? currentAppId)
    {
        if (string.IsNullOrEmpty(targetPath) || PathHelper.IsUrlScheme(targetPath))
            return null;

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

    private string? CheckPathOverlapWarning(
        string? targetPath,
        bool isAllowMode,
        List<AppEntry> existingApps,
        string? currentAppId)
    {
        if (string.IsNullOrEmpty(targetPath) || PathHelper.IsUrlScheme(targetPath))
            return null;

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

    private string? Validate(
        string? targetPath,
        bool restrictAcl,
        bool isAllowMode,
        int allowEntryCount,
        string? conflict)
    {
        if (!restrictAcl)
            return null;

        if (!string.IsNullOrEmpty(targetPath) && aclService.IsBlockedPath(targetPath))
            return $"Cannot restrict access on: {targetPath}";

        if (conflict != null)
            return conflict;

        if (isAllowMode && allowEntryCount == 0)
            return "Allow mode requires at least one entry.";

        return null;
    }

    private string? TryResolveTargetPath(string exePath, bool isFolder, AclTarget aclTarget, int depth)
    {
        if (string.IsNullOrEmpty(exePath) || PathHelper.IsUrlScheme(exePath))
            return string.IsNullOrEmpty(exePath) ? null : exePath;

        try
        {
            return aclService.ResolveAclTargetPath(new AppEntry
            {
                ExePath = exePath,
                IsFolder = isFolder,
                AclTarget = aclTarget,
                FolderAclDepth = depth
            });
        }
        catch (Exception ex)
        {
            log.Debug($"TryResolveTargetPath: failed to resolve target path for '{exePath}': {ex.Message}");
            return null;
        }
    }
}

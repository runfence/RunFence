using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Validates ACL configuration settings and detects path conflicts between apps.
/// Extracted from <see cref="AclConfigSection"/> to keep the section focused on UI.
/// </summary>
public class AclConfigValidator
{
    private readonly IAclService _aclService;

    public AclConfigValidator(IAclService aclService)
    {
        _aclService = aclService;
    }

    /// <summary>
    /// Validates ACL settings for the given path and mode. Returns an error message or null if valid.
    /// </summary>
    public string? Validate(string exePath, bool isFolder, bool restrictAcl, bool isAllowMode,
        AclTarget aclTarget, int depth, int allowEntryCount, Func<string?> checkPathConflict)
    {
        if (!restrictAcl || PathHelper.IsUrlScheme(exePath))
            return null;

        var targetPath = _aclService.ResolveAclTargetPath(new AppEntry
        {
            ExePath = exePath, IsFolder = isFolder, AclTarget = aclTarget, FolderAclDepth = depth
        });
        if (_aclService.IsBlockedPath(targetPath))
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
            targetPath = _aclService.ResolveAclTargetPath(new AppEntry
            {
                ExePath = exePath, IsFolder = isFolder, AclTarget = aclTarget, FolderAclDepth = depth
            });
        }
        catch
        {
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
                otherTarget = _aclService.ResolveAclTargetPath(other);
            }
            catch
            {
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
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Acl;

/// <summary>
/// Handles deny-mode ACL operations: computing which SIDs to deny, building deny rules,
/// and applying/removing them. Extracted from AclService to keep it focused on dispatch.
/// </summary>
public class AclDenyModeService(
    ILoggingService log,
    ILocalUserProvider localUserProvider,
    IAppContainerProfileService appContainerService)
{
    /// <summary>
    /// Returns all deny-mode apps whose resolved ACL target matches <paramref name="targetPath"/>.
    /// For folder targets, also matches apps whose target or file path is at-or-below the target.
    /// </summary>
    private List<AppEntry> GetMatchingDenyApps(string targetPath, IReadOnlyList<AppEntry> allApps,
        bool isFolderTarget, Func<AppEntry, string> resolveAclTargetPath)
    {
        var normalizedTarget = AclHelper.NormalizePath(targetPath);
        var result = new List<AppEntry>();

        foreach (var app in allApps)
        {
            if (!app.RestrictAcl || app.IsUrlScheme || app.AclMode == AclMode.Allow)
                continue;

            bool matches;
            if (isFolderTarget)
            {
                var appTarget = AclHelper.NormalizePath(resolveAclTargetPath(app));
                var appExePath = AclHelper.NormalizePath(Path.GetFullPath(app.ExePath));
                matches = appTarget.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase)
                          || AclHelper.PathIsAtOrBelow(appTarget, normalizedTarget)
                          || AclHelper.PathIsAtOrBelow(appExePath, normalizedTarget);
            }
            else
            {
                var appTarget = AclHelper.NormalizePath(resolveAclTargetPath(app));
                matches = appTarget.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
                result.Add(app);
        }

        return result;
    }

    public HashSet<string> GetAllowedSidsForPath(
        string targetPath, IReadOnlyList<AppEntry> allApps, bool isFolderTarget,
        Func<AppEntry, string> resolveAclTargetPath)
    {
        var matchingApps = GetMatchingDenyApps(targetPath, allApps, isFolderTarget, resolveAclTargetPath);
        return GetAllowedSidsFromApps(matchingApps);
    }

    private HashSet<string> GetAllowedSidsFromApps(List<AppEntry> matchingApps)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? interactiveSid = null;
        bool interactiveSidResolved = false;

        foreach (var app in matchingApps)
        {
            if (app.AppContainerName != null)
            {
                if (!interactiveSidResolved)
                {
                    interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
                    interactiveSidResolved = true;
                }

                if (interactiveSid != null)
                    allowed.Add(interactiveSid);

                // Also allow the container package SID so the sandboxed process can reach its exe
                {
                    try
                    {
                        var containerSid = appContainerService.GetSid(app.AppContainerName);
                        allowed.Add(containerSid);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"AclDenyModeService: could not resolve container SID for '{app.AppContainerName}': {ex.Message}");
                    }
                }
            }
            else
            {
                allowed.Add(app.AccountSid);
            }
        }

        return allowed;
    }

    /// <summary>
    /// Returns per-SID denied rights for a given path. For each local user SID that is NOT in the
    /// allowed set, computes the maximum DeniedRights across all matching deny-mode apps.
    /// </summary>
    public Dictionary<string, DeniedRights> GetDeniedRightsPerSid(
        string targetPath, IReadOnlyList<AppEntry> allApps, bool isFolderTarget,
        Func<AppEntry, string> resolveAclTargetPath)
    {
        var matchingApps = GetMatchingDenyApps(targetPath, allApps, isFolderTarget, resolveAclTargetPath);
        var allowedSids = GetAllowedSidsFromApps(matchingApps);
        var localUsers = localUserProvider.GetLocalUserAccounts();

        var maxRights = DeniedRights.Execute;
        foreach (var app in matchingApps)
        {
            if (app.DeniedRights > maxRights)
                maxRights = app.DeniedRights;
        }

        var result = new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in localUsers)
        {
            if (allowedSids.Contains(user.Sid))
                continue;

            try
            {
                var sid = new SecurityIdentifier(user.Sid);
                if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid))
                    continue;
            }
            catch
            {
                continue;
            }

            result[user.Sid] = maxRights;
        }

        return result;
    }

    public bool ApplyDeny(string path, bool isFolder, HashSet<string> allowedSids, DeniedRights deniedRights)
    {
        var exists = isFolder ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
            return false;

        return AclHelper.ModifyAclIf(path, isFolder, security =>
            ApplyDenyRules(security, localUserProvider.GetLocalUserAccounts(), allowedSids, isFolder, deniedRights));
    }

    public bool ApplyDenyToFolderPerSid(string folderPath, Dictionary<string, DeniedRights> deniedRightsPerSid)
    {
        if (!Directory.Exists(folderPath))
            return false;

        var desiredRules = new List<FileSystemAccessRule>();
        foreach (var (sidString, rights) in deniedRightsPerSid)
        {
            try
            {
                var sid = new SecurityIdentifier(sidString);
                var fileRights = AclRightsHelper.MapDeniedRights(rights);
                desiredRules.Add(new FileSystemAccessRule(
                    sid, fileRights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Deny));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to create deny rule for SID {sidString}", ex);
            }
        }

        var localUsers = localUserProvider.GetLocalUserAccounts();
        var managedSidObjects = AclHelper.BuildLocalUserSidSet(localUsers);
        foreach (var s in deniedRightsPerSid.Keys)
        {
            try
            {
                managedSidObjects.Add(new SecurityIdentifier(s));
            }
            catch (ArgumentException)
            {
            }
        }

        var dirInfo = new DirectoryInfo(folderPath);
        var security = dirInfo.GetAccessControl();
        if (!AclHelper.ApplyAclDiff(security, desiredRules, rule =>
                rule is { AccessControlType: AccessControlType.Deny, IdentityReference: SecurityIdentifier sid } &&
                managedSidObjects.Contains(sid) &&
                (rule.FileSystemRights & AclRightsHelper.ManagedDenyRightsMask) != 0))
        {
            return false;
        }

        dirInfo.SetAccessControl(security);
        return true;
    }

    private bool ApplyDenyRules(FileSystemSecurity security, List<LocalUserAccount> localUsers,
        HashSet<string> allowedSids, bool isFolder, DeniedRights deniedRights)
    {
        var fileRights = AclRightsHelper.MapDeniedRights(deniedRights);
        var desiredRules = new List<FileSystemAccessRule>();

        foreach (var user in localUsers)
        {
            if (allowedSids.Contains(user.Sid))
                continue;

            try
            {
                var sid = new SecurityIdentifier(user.Sid);
                if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid))
                    continue;

                desiredRules.Add(new FileSystemAccessRule(
                    sid, fileRights,
                    AclHelper.InheritanceFlagsFor(isFolder), PropagationFlags.None,
                    AccessControlType.Deny));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to add Deny for user {user.Username}", ex);
            }
        }

        var knownSids = AclHelper.BuildLocalUserSidSet(localUsers);

        return AclHelper.ApplyAclDiff(security, desiredRules, rule =>
            rule is { AccessControlType: AccessControlType.Deny, IdentityReference: SecurityIdentifier sid } &&
            knownSids.Contains(sid) &&
            (rule.FileSystemRights & AclRightsHelper.ManagedDenyRightsMask) != 0);
    }

    public void RemoveManagedDenyAces(string path, bool isFolder)
    {
        try
        {
            var knownSids = AclHelper.BuildLocalUserSidSet(localUserProvider.GetLocalUserAccounts());

            var exists = isFolder ? Directory.Exists(path) : File.Exists(path);
            if (!exists)
                return;

            AclHelper.ModifyAclIf(path, isFolder, security => RemoveManagedDenyAces(security, knownSids));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to remove Deny ACEs from {path}", ex);
        }
    }

    public static bool RemoveManagedDenyAces(FileSystemSecurity security, HashSet<SecurityIdentifier> knownSids)
    {
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        var changed = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Deny &&
                rule.IdentityReference is SecurityIdentifier sid &&
                knownSids.Contains(sid) &&
                (rule.FileSystemRights & AclRightsHelper.ManagedDenyRightsMask) != 0)
            {
                security.RemoveAccessRuleSpecific(rule);
                changed = true;
            }
        }

        return changed;
    }
}
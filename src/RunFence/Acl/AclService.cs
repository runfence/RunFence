using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public class AclService(
    ILoggingService log,
    IAclDenyModeService denyService,
    IAclAllowModeService allowService,
    IDatabaseProvider databaseProvider)
    : IAclService
{
    public void ApplyAcl(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app.IsUrlScheme || !app.RestrictAcl)
            return;
        if (app is { IsFolder: true, AclTarget: AclTarget.File })
            return;

        var targetPath = ResolveAclTargetPath(app);
        if (IsBlockedPath(targetPath))
        {
            log.Warn($"Blocked ACL target path: {targetPath}");
            return;
        }

        // Allow mode: standalone, no deny-mode combining needed
        if (app.AclMode == AclMode.Allow)
        {
            if (allowService.ApplyAllowAcl(app, targetPath))
                log.Info($"Applied allow-mode ACL to {targetPath} for app {app.Name}");

            // Also grant AppContainer SID ReadAndExecute so the sandboxed process can access its exe
            if (app.AppContainerName != null)
                TryGrantContainerSid(app.AppContainerName, targetPath);

            return;
        }

        // Deny mode: clean up any leftover allow-mode ACEs first (e.g., if mode was switched).
        // Must happen before TryGrantContainerSid — CleanupAllowModeAces removes all explicit
        // allow ACEs when inheritance is broken, which would wipe the container SID grant.
        allowService.CleanupAllowModeAces(targetPath, app.AclTarget == AclTarget.Folder);

        // Deny mode with AppContainer: grant the container SID so it can reach its exe
        if (app.AppContainerName != null)
            TryGrantContainerSid(app.AppContainerName, targetPath);

        // Deny mode
        var isFolderTarget = app.AclTarget == AclTarget.Folder;
        var allowedSids = denyService.GetAllowedSidsForPath(targetPath, allApps, isFolderTarget, ResolveAclTargetPath);

        bool aclChanged = denyService.ApplyDeny(targetPath, isFolderTarget, allowedSids, app.DeniedRights);

        if (aclChanged)
            log.Info($"Applied deny ACL to {targetPath} for app {app.Name}");
    }

    public void RevertAcl(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app.IsUrlScheme || !app.RestrictAcl)
            return;
        if (app is { IsFolder: true, AclTarget: AclTarget.File })
            return;

        var targetPath = ResolveAclTargetPath(app);
        if (IsBlockedPath(targetPath))
        {
            log.Warn($"Blocked ACL target path on revert: {targetPath}");
            return;
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return;

        // Allow mode: standalone revert, no combining
        if (app.AclMode == AclMode.Allow)
        {
            // Revoke the container SID grant added separately by ApplyAcl (not part of AllowedAclEntries)
            if (app.AppContainerName != null)
                TryRevokeContainerSid(app.AppContainerName, targetPath);
            allowService.RevertAllowAcl(targetPath, app);
            log.Info($"Reverted allow-mode ACL on {targetPath} for app {app.Name}");
            return;
        }

        // Remove AppContainer SID grant on revert (deny mode)
        if (app.AppContainerName != null)
            TryRevokeContainerSid(app.AppContainerName, targetPath);

        // Deny mode: filter otherAppsOnSamePath to deny-mode only
        var remainingApps = allApps.Where(a => a.Id != app.Id).ToList();

        var otherAppsOnSamePath = remainingApps.Where(a =>
                a is { RestrictAcl: true, IsUrlScheme: false } &&
                a.AclMode != AclMode.Allow &&
                AclHelper.NormalizePath(ResolveAclTargetPath(a)).Equals(AclHelper.NormalizePath(targetPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (otherAppsOnSamePath.Count > 0)
        {
            // Apply the most restrictive deny level among remaining apps on this path,
            // so that removing one app does not downgrade protection.
            var mostRestrictive = otherAppsOnSamePath.OrderByDescending(a => a.DeniedRights).First();
            ApplyAcl(mostRestrictive, remainingApps);
        }
        else
        {
            denyService.RemoveManagedDenyAces(targetPath, app.AclTarget == AclTarget.Folder);
        }

        log.Info($"Reverted ACL on {targetPath} for app {app.Name}");
    }

    public void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps)
    {
        var folderApps = allApps.Where(a =>
            a is { RestrictAcl: true, IsUrlScheme: false, AclTarget: AclTarget.Folder } && a.AclMode != AclMode.Allow).ToList();

        if (folderApps.Count == 0)
            return;

        var recomputedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderApp in folderApps)
        {
            var ancestorPath = AclHelper.NormalizePath(ResolveAclTargetPath(folderApp));

            if (recomputedPaths.Contains(ancestorPath))
                continue;

            // Check if any other app's exe is under this folder (making it an ancestor)
            var hasDescendantExe = allApps.Any(a =>
                a.Id != folderApp.Id &&
                a is { RestrictAcl: true, IsUrlScheme: false } &&
                a.AclMode != AclMode.Allow &&
                AclHelper.PathIsAtOrBelow(AclHelper.NormalizePath(Path.GetFullPath(a.ExePath)), ancestorPath));

            if (!hasDescendantExe)
                continue;

            if (IsBlockedPath(ancestorPath))
            {
                log.Warn($"Blocked ancestor ACL target path: {ancestorPath}");
                continue;
            }

            var deniedRightsPerSid = denyService.GetDeniedRightsPerSid(ancestorPath, allApps, isFolderTarget: true, ResolveAclTargetPath);
            if (denyService.ApplyDenyToFolderPerSid(ancestorPath, deniedRightsPerSid))
                log.Info($"Recomputed ancestor ACL for folder {ancestorPath}");
            recomputedPaths.Add(ancestorPath);
        }
    }

    public bool IsBlockedPath(string resolvedPath)
    {
        var normalized = Path.GetFullPath(resolvedPath).TrimEnd(Path.DirectorySeparatorChar);

        if (Constants.GetBlockedAclPaths().Select(blocked => Path.GetFullPath(blocked).TrimEnd(Path.DirectorySeparatorChar))
            .Any(normalizedBlocked => string.Equals(normalized, normalizedBlocked, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windowsDir.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public string ResolveAclTargetPath(AppEntry app)
    {
        if (app.AclTarget == AclTarget.File)
            return Path.GetFullPath(app.ExePath);

        var folder = app.IsFolder
            ? Path.GetFullPath(app.ExePath)
            : Path.GetDirectoryName(Path.GetFullPath(app.ExePath))!;
        var cappedDepth = Math.Min(app.FolderAclDepth, Constants.MaxFolderAclDepth);
        for (int i = 0; i < cappedDepth; i++)
        {
            var parent = Path.GetDirectoryName(folder);
            if (parent == null)
                break;
            folder = parent;
        }

        return folder;
    }

    /// <summary>
    /// Returns the set of SIDs that are allowed (not denied) for a given path.
    /// This method exists solely to give tests direct access to <see cref="IAclDenyModeService.GetAllowedSidsForPath"/>;
    /// it is NOT part of the <see cref="IAclService"/> interface.
    /// </summary>
    public HashSet<string> GetAllowedSidsForPath(
        string targetPath, IReadOnlyList<AppEntry> allApps, bool isFolderTarget)
        => denyService.GetAllowedSidsForPath(targetPath, allApps, isFolderTarget, ResolveAclTargetPath);

    // --- AppContainer SID helpers ---

    private void TryGrantContainerSid(string containerName, string targetPath)
    {
        TryModifyContainerSid(containerName, targetPath, "grant",
            (sid, isDirectory) =>
            {
                var inhFlags = AclHelper.InheritanceFlagsFor(isDirectory);
                var rule = new FileSystemAccessRule(
                    sid,
                    FileSystemRights.ReadAndExecute,
                    inhFlags, PropagationFlags.None, AccessControlType.Allow);
                AclHelper.ModifyAcl(targetPath, isDirectory, security => security.AddAccessRule(rule));
                log.Info($"Granted AppContainer SID '{sid.Value}' ReadAndExecute on '{targetPath}'");
            });
    }

    private void TryRevokeContainerSid(string containerName, string targetPath)
    {
        TryModifyContainerSid(containerName, targetPath, "revoke",
            (sid, isDirectory) =>
            {
                AclHelper.ModifyAcl(targetPath, isDirectory, security =>
                {
                    var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                    foreach (FileSystemAccessRule rule in rules)
                    {
                        if (rule.IdentityReference is SecurityIdentifier ruleSid && ruleSid == sid)
                            security.RemoveAccessRuleSpecific(rule);
                    }
                });
                log.Info($"Revoked AppContainer SID '{sid.Value}' from '{targetPath}'");
            });
    }

    /// <summary>
    /// Resolves the AppContainer SID for <paramref name="containerName"/>, verifies the target path
    /// exists, then invokes <paramref name="operation"/> with the resolved SID and isDirectory flag.
    /// Logs and swallows exceptions so ACL failures never block launch.
    /// </summary>
    private void TryModifyContainerSid(
        string containerName, string targetPath, string actionName,
        Action<SecurityIdentifier, bool> operation)
    {
        try
        {
            var containerSid = ResolveContainerSid(containerName);
            if (containerSid == null)
            {
                log.Warn($"AppContainer SID not resolved for '{containerName}' — skipping {actionName} on '{targetPath}'");
                return;
            }

            var isDirectory = Directory.Exists(targetPath);
            if (!isDirectory && !File.Exists(targetPath))
                return;

            var sid = new SecurityIdentifier(containerSid);
            operation(sid, isDirectory);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to {actionName} AppContainer SID for '{containerName}' on '{targetPath}': {ex.Message}");
        }
    }

    private string? ResolveContainerSid(string containerName)
        => AclHelper.ResolveContainerSid(databaseProvider.GetDatabase(), containerName);
}
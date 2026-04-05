using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Acl;

public class AclService : IAclService
{
    private readonly ILoggingService _log;
    private readonly IAppContainerService _appContainerService;
    private readonly AclDenyModeService _denyService;
    private readonly AclAllowModeService _allowService;

    public AclService(ILoggingService log, AclDenyModeService denyService,
        AclAllowModeService allowService,
        IAppContainerService appContainerService)
    {
        _log = log;
        _appContainerService = appContainerService;
        _denyService = denyService;
        _allowService = allowService;
    }

    public void ApplyAcl(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app.IsUrlScheme || !app.RestrictAcl)
            return;
        if (app is { IsFolder: true, AclTarget: AclTarget.File })
            return;

        var targetPath = ResolveAclTargetPath(app);
        if (IsBlockedPath(targetPath))
        {
            _log.Warn($"Blocked ACL target path: {targetPath}");
            return;
        }

        // Allow mode: standalone, no deny-mode combining needed
        if (app.AclMode == AclMode.Allow)
        {
            if (_allowService.ApplyAllowAcl(app, targetPath))
                _log.Info($"Applied allow-mode ACL to {targetPath} for app {app.Name}");

            // Also grant AppContainer SID ReadAndExecute so the sandboxed process can access its exe
            if (app.AppContainerName != null)
                TryGrantContainerSid(app.AppContainerName, targetPath);

            return;
        }

        // Deny mode: clean up any leftover allow-mode ACEs first (e.g., if mode was switched).
        // Must happen before TryGrantContainerSid — CleanupAllowModeAces removes all explicit
        // allow ACEs when inheritance is broken, which would wipe the container SID grant.
        _allowService.CleanupAllowModeAces(targetPath, app.AclTarget == AclTarget.Folder);

        // Deny mode with AppContainer: grant the container SID so it can reach its exe
        if (app.AppContainerName != null)
            TryGrantContainerSid(app.AppContainerName, targetPath);

        // Deny mode
        var isFolderTarget = app.AclTarget == AclTarget.Folder;
        var allowedSids = _denyService.GetAllowedSidsForPath(targetPath, allApps, isFolderTarget, ResolveAclTargetPath);

        bool aclChanged = _denyService.ApplyDeny(targetPath, isFolderTarget, allowedSids, app.DeniedRights);

        if (aclChanged)
            _log.Info($"Applied deny ACL to {targetPath} for app {app.Name}");
    }

    public void RevertAcl(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app.IsUrlScheme || !app.RestrictAcl)
            return;

        var targetPath = ResolveAclTargetPath(app);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return;

        // Allow mode: standalone revert, no combining
        if (app.AclMode == AclMode.Allow)
        {
            // Revoke the container SID grant added separately by ApplyAcl (not part of AllowedAclEntries)
            if (app.AppContainerName != null)
                TryRevokeContainerSid(app.AppContainerName, targetPath);
            _allowService.RevertAllowAcl(targetPath, app);
            _log.Info($"Reverted allow-mode ACL on {targetPath} for app {app.Name}");
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
            _denyService.RemoveManagedDenyAces(targetPath, app.AclTarget == AclTarget.Folder);
        }

        _log.Info($"Reverted ACL on {targetPath} for app {app.Name}");
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
                _log.Warn($"Blocked ancestor ACL target path: {ancestorPath}");
                continue;
            }

            var deniedRightsPerSid = _denyService.GetDeniedRightsPerSid(ancestorPath, allApps, isFolderTarget: true, ResolveAclTargetPath);
            if (_denyService.ApplyDenyToFolderPerSid(ancestorPath, deniedRightsPerSid))
                _log.Info($"Recomputed ancestor ACL for folder {ancestorPath}");
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
    /// Exposed internally for tests.
    /// </summary>
    public HashSet<string> GetAllowedSidsForPath(
        string targetPath, IReadOnlyList<AppEntry> allApps, bool isFolderTarget)
        => _denyService.GetAllowedSidsForPath(targetPath, allApps, isFolderTarget, ResolveAclTargetPath);

    // --- AppContainer SID helpers ---

    private void TryGrantContainerSid(string containerName, string targetPath)
    {
        try
        {
            var containerSid = _appContainerService.GetSid(containerName);
            var sid = new SecurityIdentifier(containerSid);
            var isDirectory = Directory.Exists(targetPath);
            var isFile = File.Exists(targetPath);
            if (!isDirectory && !isFile)
                return;

            var inhFlags = AclHelper.InheritanceFlagsFor(isDirectory);

            var rule = new FileSystemAccessRule(
                sid,
                FileSystemRights.ReadAndExecute,
                inhFlags, PropagationFlags.None, AccessControlType.Allow);

            AclHelper.ModifyAcl(targetPath, isDirectory, security => security.AddAccessRule(rule));
            _log.Info($"Granted AppContainer SID '{containerSid}' ReadAndExecute on '{targetPath}'");
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to grant AppContainer SID for '{containerName}' on '{targetPath}': {ex.Message}");
        }
    }

    private void TryRevokeContainerSid(string containerName, string targetPath)
    {
        try
        {
            var containerSid = _appContainerService.GetSid(containerName);
            var sid = new SecurityIdentifier(containerSid);
            var isDirectory = Directory.Exists(targetPath);
            var isFile = File.Exists(targetPath);
            if (!isDirectory && !isFile)
                return;

            AclHelper.ModifyAcl(targetPath, isDirectory, security =>
            {
                var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.IdentityReference is SecurityIdentifier ruleSid && ruleSid == sid)
                        security.RemoveAccessRuleSpecific(rule);
                }
            });
            _log.Info($"Revoked AppContainer SID '{containerSid}' from '{targetPath}'");
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to revoke AppContainer SID for '{containerName}' on '{targetPath}': {ex.Message}");
        }
    }
}
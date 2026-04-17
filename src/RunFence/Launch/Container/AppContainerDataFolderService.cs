using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Manages the per-container data folder under ProgramData (creating, ACL-ing, and granting
/// the interactive user access). Separated from AppContainerService to keep profile lifecycle
/// logic distinct from data folder lifecycle logic.
/// </summary>
public class AppContainerDataFolderService(ILoggingService log, IPathGrantService pathGrantService)
{
    /// <summary>
    /// Creates the container's data folder subtree (Temp/Roaming/Local/ProgramData) if missing,
    /// grants the container SID FullControl, and ensures traverse access on ancestor directories.
    /// Idempotent — safe to call on every launch.
    /// </summary>
    public void EnsureContainerDataFolder(AppContainerEntry entry, string containerSid)
    {
        EnsureContainersRootAcl();
        var dataRoot = AppContainerPaths.GetContainerDataPath(entry.Name);

        foreach (var subDir in new[] { "Temp", "Roaming", "Local", "ProgramData" })
            Directory.CreateDirectory(Path.Combine(dataRoot, subDir));

        try
        {
            var identity = new SecurityIdentifier(containerSid);
            GrantFullControlRecursive(dataRoot, identity);

            // Grant traverse on intermediate directories so the AppContainer token can
            // reach its data folder. AppContainer dual-check requires an ACE on every
            // directory in the path — SeChangeNotifyPrivilege doesn't help here.
            // Tracking in AccountGrants allows RevertTraverseAccess to clean up naturally.
            pathGrantService.AddTraverse(containerSid, dataRoot);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to set ACL on container data folder for '{entry.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Re-grants traverse access on the data folder's ancestor directories for the container SID.
    /// Called on every launch (idempotent — skips directories that already have the container SID).
    /// FullControl on the data dirs themselves is set once by <see cref="EnsureContainerDataFolder"/>.
    /// </summary>
    public void EnsureDataFolderTraverse(AppContainerEntry entry, string containerSid)
    {
        try
        {
            var dataPath = AppContainerPaths.GetContainerDataPath(entry.Name);
            if (!Directory.Exists(dataPath))
                return;

            pathGrantService.AddTraverse(containerSid, dataPath);
        }
        catch (Exception ex)
        {
            log.Warn($"EnsureDataFolderTraverse failed for '{entry.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Grants the interactive (desktop) user FullControl on the container's data folder.
    /// AppContainer tokens use a dual access check — both the user SID (step 1) and
    /// the container SID (step 2) must independently have access. When elevated ≠ interactive,
    /// the interactive user may lack access to the data folder under ProgramData.
    /// SeChangeNotifyPrivilege bypasses traverse checking for step 1,
    /// so only the target directory needs an ACE — not ancestors.
    /// </summary>
    public void EnsureInteractiveUserAccess(AppContainerEntry entry)
    {
        try
        {
            var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid();
            if (interactiveSid == null)
                return;

            var currentSid = SidResolutionHelper.GetCurrentUserSid();
            if (string.Equals(interactiveSid.Value, currentSid, StringComparison.OrdinalIgnoreCase))
                return;

            var dataRoot = AppContainerPaths.GetContainerDataPath(entry.Name);
            if (!Directory.Exists(dataRoot))
                return;

            GrantFullControlRecursive(dataRoot, interactiveSid);
        }
        catch (Exception ex)
        {
            log.Warn($"EnsureInteractiveUserAccess failed for '{entry.Name}': {ex.Message}");
        }
    }

    private void EnsureContainersRootAcl()
    {
        try
        {
            var root = AppContainerPaths.GetContainersRootPath();
            var dirInfo = new DirectoryInfo(root);
            dirInfo.Create();
            var security = dirInfo.GetAccessControl();
            if (security.AreAccessRulesProtected)
                return; // already locked down

            security.SetAccessRuleProtection(true, false);
            var existingRules = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().ToList();
            foreach (var rule in existingRules)
                security.RemoveAccessRule(rule);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            log.Warn($"EnsureContainersRootAcl failed: {ex.Message}");
        }
    }

    private static void GrantFullControlRecursive(string path, IdentityReference identity)
    {
        var dirInfo = new DirectoryInfo(path);
        var security = dirInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        dirInfo.SetAccessControl(security);
    }
}
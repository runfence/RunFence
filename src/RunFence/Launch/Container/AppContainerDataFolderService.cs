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
public class AppContainerDataFolderService(
    IPathGrantService pathGrantService,
    IAclAccessor aclAccessor,
    IAppContainerPathProvider pathProvider)
    : IAppContainerDataFolderService
{
    private readonly IAppContainerPathProvider _pathProvider = pathProvider;
    private readonly string _containersRootPath = pathProvider.GetContainersRootPath();

    /// <summary>
    /// Creates the container's data folder subtree (Temp/Roaming/Local/ProgramData) if missing,
    /// grants the container SID FullControl, and ensures traverse access on ancestor directories.
    /// Idempotent - safe to call on every launch.
    /// </summary>
    public void EnsureContainerDataFolder(AppContainerEntry entry, string containerSid)
    {
        EnsureContainersRootAcl();
        var dataRoot = GetContainerDataPath(entry.Name);

        foreach (var subDir in new[] { "Temp", "Roaming", "Local", "ProgramData" })
            Directory.CreateDirectory(Path.Combine(dataRoot, subDir));

        EnsureContainerDataRootGrant(containerSid, dataRoot);

        // Grant traverse on intermediate directories so the AppContainer token can
        // reach its data folder. AppContainer dual-check requires an ACE on every
        // directory in the path - SeChangeNotifyPrivilege doesn't help here.
        // Tracking in AccountGrants allows RevertTraverseAccess to clean up naturally.
        pathGrantService.AddTraverse(containerSid, dataRoot);
        VerifyExplicitAllowRule(dataRoot, containerSid, ContainerDataRootRights);
    }

    /// <summary>
    /// Re-grants traverse access on the data folder's ancestor directories for the container SID.
    /// Called on every launch (idempotent - skips directories that already have the container SID).
    /// FullControl on the data dirs themselves is set once by <see cref="EnsureContainerDataFolder"/>.
    /// </summary>
    public void EnsureDataFolderTraverse(AppContainerEntry entry, string containerSid)
    {
        var dataPath = GetContainerDataPath(entry.Name);
        if (!Directory.Exists(dataPath))
            return;

        EnsureContainerDataRootGrant(containerSid, dataPath);
        pathGrantService.AddTraverse(containerSid, dataPath);
        VerifyExplicitAllowRule(dataPath, containerSid, ContainerDataRootRights);
    }

    /// <summary>
    /// Grants the interactive (desktop) user FullControl on the container's data folder.
    /// AppContainer tokens use a dual access check - both the user SID (step 1) and
    /// the container SID (step 2) must independently have access. When elevated != interactive,
    /// the interactive user may lack access to the data folder under ProgramData.
    /// SeChangeNotifyPrivilege bypasses traverse checking for step 1,
    /// so only the target directory needs an ACE - not ancestors.
    /// </summary>
    public void EnsureInteractiveUserAccess(AppContainerEntry entry)
    {
        var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid();
        if (interactiveSid == null)
            return;

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        if (string.Equals(interactiveSid.Value, currentSid, StringComparison.OrdinalIgnoreCase))
            return;

        var dataRoot = GetContainerDataPath(entry.Name);
        if (!Directory.Exists(dataRoot))
            return;

        pathGrantService.AddGrant(
            interactiveSid.Value,
            dataRoot,
            isDeny: false,
            ContainerDataRootRights,
            confirm: null);
        VerifyExplicitAllowRule(dataRoot, interactiveSid.Value, ContainerDataRootRights);
    }

    private void EnsureContainerDataRootGrant(string containerSid, string dataPath) =>
        pathGrantService.AddGrant(
            containerSid,
            dataPath,
            isDeny: false,
            ContainerDataRootRights,
            confirm: null);

    private static readonly SavedRightsState ContainerDataRootRights =
        new(Execute: true, Write: true, Read: true, Special: true, Own: false);

    private void EnsureContainersRootAcl()
    {
        Directory.CreateDirectory(_containersRootPath);
        aclAccessor.ModifyAclWithFallback(_containersRootPath, isFolder: true, security =>
        {
            if (IsContainersRootAclHardened(security))
                return false;

            security.SetAccessRuleProtection(true, false);
            var existingRules = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            foreach (var rule in existingRules)
                security.RemoveAccessRule(rule);

            security.AddAccessRule(CreateFullControlRule(WellKnownSidType.BuiltinAdministratorsSid));
            security.AddAccessRule(CreateFullControlRule(WellKnownSidType.LocalSystemSid));
            AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(
                security,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None);
            return true;
        });

        var hardenedSecurity = aclAccessor.GetSecurity(_containersRootPath);
        if (!IsContainersRootAclHardened(hardenedSecurity))
            throw new InvalidOperationException($"AppContainer containers root ACL verification failed for '{_containersRootPath}'.");
    }

    private string GetContainerDataPath(string profileName)
        => _pathProvider.GetContainerDataPath(profileName);

    private void VerifyExplicitAllowRule(string path, string sid, SavedRightsState rights)
    {
        var security = aclAccessor.GetSecurity(path);
        var expectedRights = GrantRightsMapper.MapAllowRights(rights, isFolder: true);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();
        if (!rules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier identifier &&
                string.Equals(identifier.Value, sid, StringComparison.OrdinalIgnoreCase) &&
                (rule.FileSystemRights & expectedRights) == expectedRights))
        {
            throw new InvalidOperationException(
                $"ACL verification failed for '{path}'. Expected allow rights '{expectedRights}' for SID '{sid}'.");
        }
    }

    private static bool IsContainersRootAclHardened(FileSystemSecurity security)
    {
        if (!security.AreAccessRulesProtected)
            return false;

        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        var expectedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value
        };
        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
            expectedSids.Add(currentMockSid.Value);

        if (rules.Count != expectedSids.Count)
            return false;

        return rules.All(rule => IsExpectedRootRule(rule, expectedSids));
    }

    private static bool IsExpectedRootRule(FileSystemAccessRule rule, HashSet<string> expectedSids)
    {
        if (rule.AccessControlType != AccessControlType.Allow)
            return false;
        if (rule.IdentityReference is not SecurityIdentifier sid)
            return false;
        if (rule.FileSystemRights != FileSystemRights.FullControl)
            return false;
        if (rule.InheritanceFlags != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
            return false;
        if (rule.PropagationFlags != PropagationFlags.None)
            return false;

        return expectedSids.Contains(sid.Value);
    }

    private static FileSystemAccessRule CreateFullControlRule(WellKnownSidType sidType)
        => new(
            new SecurityIdentifier(sidType, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
}

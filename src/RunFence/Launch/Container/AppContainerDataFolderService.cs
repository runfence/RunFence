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
    IGrantMutatorService grantMutatorService,
    ITraverseService traverseService,
    IPathSecurityDescriptorAccessor aclAccessor,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataManagedObjectRepairService programDataManagedObjectRepairService,
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
        programDataDirectoryProvisioningService.EnsureRoot();
        EnsureContainersRootAcl();
        var dataRoot = GetContainerDataPath(entry.Name);
        var expectedOwnerSids = GetExpectedProfileOwnerSids(containerSid);

        if (Directory.Exists(dataRoot))
            programDataManagedObjectRepairService.EnsureManagedDirectoryOwner(dataRoot, expectedOwnerSids);

        foreach (var subDir in new[] { "Temp", "Roaming", "Local", "ProgramData" })
            Directory.CreateDirectory(Path.Combine(dataRoot, subDir));

        EnsureProfileTreeOwners(dataRoot, expectedOwnerSids);
        EnsureContainerDataRootGrant(containerSid, dataRoot);

        // Grant traverse on intermediate directories so the AppContainer token can
        // reach its data folder. AppContainer dual-check requires an ACE on every
        // directory in the path - SeChangeNotifyPrivilege doesn't help here.
        // Tracking in AccountGrants allows RevertTraverseAccess to clean up naturally.
        traverseService.AddTraverse(containerSid, dataRoot);
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
        traverseService.AddTraverse(containerSid, dataPath);
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

        grantMutatorService.AddGrant(
            interactiveSid.Value,
            dataRoot,
            isDeny: false,
            ContainerDataRootRights,
            confirm: null);
        VerifyExplicitAllowRule(dataRoot, interactiveSid.Value, ContainerDataRootRights);
    }

    private void EnsureContainerDataRootGrant(string containerSid, string dataPath) =>
        grantMutatorService.AddGrant(
            containerSid,
            dataPath,
            isDeny: false,
            ContainerDataRootRights,
            confirm: null);

    private static readonly SavedRightsState ContainerDataRootRights =
        new(Execute: true, Write: true, Read: true, Special: true, Own: false);

    private void EnsureContainersRootAcl()
    {
        var managedAcRoot = programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.Ac);
        if (!string.Equals(
                Path.GetFullPath(managedAcRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(_containersRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AppContainer containers root path '{_containersRootPath}' does not match managed ProgramData AC root '{managedAcRoot}'.");
        }
    }

    private void EnsureProfileTreeOwners(string dataRoot, IReadOnlyCollection<string> expectedOwnerSids)
    {
        programDataManagedObjectRepairService.EnsureManagedDirectoryOwner(dataRoot, expectedOwnerSids);

        foreach (var subDir in new[] { "Temp", "Roaming", "Local", "ProgramData" })
        {
            programDataManagedObjectRepairService.EnsureManagedDirectoryOwner(
                Path.Combine(dataRoot, subDir),
                expectedOwnerSids);
        }
    }

    private static IReadOnlyCollection<string> GetExpectedProfileOwnerSids(string containerSid)
    {
        var expectedOwnerSids = new List<string> { new SecurityIdentifier(containerSid).Value };
        var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid();
        if (interactiveSid != null &&
            !expectedOwnerSids.Contains(interactiveSid.Value, StringComparer.OrdinalIgnoreCase))
        {
            expectedOwnerSids.Add(interactiveSid.Value);
        }

        return expectedOwnerSids;
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

}

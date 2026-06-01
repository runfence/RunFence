using RunFence.Acl;

namespace RunFence.Tests;

public sealed class ProgramDataSecurityTestFacade(
    ProgramDataDirectoryProvisioner directoryProvisioner,
    IProgramDataObjectProvisioner objectProvisioner,
    IProgramDataManagedObjectRepairService managedObjectRepairer,
    IProgramDataPathPolicyService pathPolicyService)
{
    public string EnsureRoot() => directoryProvisioner.EnsureRoot();

    public string EnsureSubdirectory(string relativePath, ProgramDataDirectoryAclProfile aclProfile)
    {
        var directoryPath = Path.Combine(directoryProvisioner.EnsureRoot(), relativePath);
        directoryProvisioner.EnsureDirectoryUnderRoot(directoryPath, aclProfile);
        return directoryPath;
    }

    public string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy)
        => directoryProvisioner.EnsureKnownDirectory(policy);

    public void EnsureDirectoryUnderRoot(string directoryPath, ProgramDataDirectoryAclProfile aclProfile)
        => directoryProvisioner.EnsureDirectoryUnderRoot(directoryPath, aclProfile);

    public void EnsureDirectoryTreeInheritsFromRoot(
        string directoryPath,
        ProgramDataDirectoryAclProfile rootAclProfile)
        => directoryProvisioner.EnsureDirectoryTreeInheritsFromRoot(directoryPath, rootAclProfile);

    public void EnsureTraverseOnlyAccess(string directoryPath, string sid, ProgramDataDirectoryAclProfile aclProfile)
        => directoryProvisioner.EnsureTraverseOnlyAccess(directoryPath, sid, aclProfile);

    public FileStream CreateOrReplaceManagedFile(
        string filePath,
        ProgramDataFileAclProfile fileAclProfile)
        => objectProvisioner.CreateOrReplaceFile(
            new ProgramDataExplicitFileRequest(
                filePath,
                fileAclProfile,
                [],
                FileShare.Read,
                OverwriteExisting: true));

    public bool EnsureManagedFileOwner(string filePath)
        => managedObjectRepairer.EnsureManagedFileOwner(filePath);

    public bool EnsureManagedDirectoryOwner(string directoryPath)
        => managedObjectRepairer.EnsureManagedDirectoryOwner(directoryPath);

    public ProgramDataSecurityRepairResult EnsureManagedFileSecurity(
        string filePath,
        ProgramDataFileAclProfile aclProfile)
        => managedObjectRepairer.EnsureManagedFileSecurity(filePath, aclProfile);

    public bool EnsureManagedFileOwner(
        string filePath,
        IReadOnlyCollection<string> expectedAdditionalOwnerSids)
        => managedObjectRepairer.EnsureManagedFileOwner(filePath, expectedAdditionalOwnerSids);

    public bool EnsureManagedDirectoryOwner(
        string directoryPath,
        IReadOnlyCollection<string> expectedAdditionalOwnerSids)
        => managedObjectRepairer.EnsureManagedDirectoryOwner(directoryPath, expectedAdditionalOwnerSids);

    public bool IsUnderRoot(string path) => pathPolicyService.IsUnderRoot(path);
}

namespace RunFence.Acl;

public interface IProgramDataManagedObjectRepairService
{
    bool EnsureManagedFileOwner(string filePath);
    bool EnsureManagedDirectoryOwner(string directoryPath);
    ProgramDataSecurityRepairResult EnsureManagedFileSecurity(string filePath, ProgramDataFileAclProfile aclProfile);
    bool EnsureManagedFileOwner(string filePath, IReadOnlyCollection<string> expectedAdditionalOwnerSids);
    bool EnsureManagedDirectoryOwner(string directoryPath, IReadOnlyCollection<string> expectedAdditionalOwnerSids);
}
